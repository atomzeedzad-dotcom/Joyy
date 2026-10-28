using System.Diagnostics;
using Joyy.Platform;
using Microsoft.Win32;

namespace Joyy.App;

/// <summary>
/// Per-user install: copies Joyy to %LOCALAPPDATA%\Programs\Joyy, adds a Start-menu shortcut named
/// "Joyy" (so typing Joyy in Windows search finds it), an "Apps &amp; features" entry, and starts it
/// with Windows. No administrator rights needed.
/// </summary>
internal static class Installer
{
    public const string AppName = "Joyy";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Joyy";

    public static string InstallDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    public static string InstalledExe => Path.Combine(InstallDir, "Joyy.exe");

    static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Joyy.lnk");

    public static bool IsInstalled => File.Exists(InstalledExe);

    public static bool RunningFromInstallDir =>
        string.Equals(Path.GetDirectoryName(Environment.ProcessPath), InstallDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Installs or updates, then starts the installed copy (unless <paramref name="launch"/> is false).
    /// <paramref name="autostart"/>: true = also start with Windows, false = remove that, null = leave as it is.
    /// Starting with Windows is never switched on unless the user chose it.
    /// </summary>
    public static int Install(bool launch, bool quiet, bool? autostart)
    {
        try
        {
            string source = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find Joyy.exe.");
            if (!RunningFromInstallDir)
            {
                QuitRunning();
                CopyFiles(source);
            }

            CreateShortcut();
            RegisterUninstall();
            if (autostart is { } start)
                StartupRegistration.Set(start, InstalledExe);
            else if (StartupRegistration.IsEnabled)
                StartupRegistration.Set(true, InstalledExe); // keep an existing choice, but point it at the installed copy

            if (launch)
                Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = false, WorkingDirectory = InstallDir })?.Dispose();
            if (!quiet)
                MessageBox.Show("Joyy is installed.\n\nOpen it any time by typing \"Joyy\" in the Start menu search." +
                                (StartupRegistration.IsEnabled
                                    ? "\nIt starts with Windows, so your DualSense works as an Xbox controller without opening anything."
                                    : "\nTurn on \"Start Joyy with Windows\" in Joyy's Settings so the controller works without opening it first."),
                    "Joyy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            if (!quiet)
                MessageBox.Show($"Could not install Joyy:\n\n{ex.Message}", "Joyy", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    public static int Uninstall(bool quiet)
    {
        try
        {
            QuitRunning();

            // Give the DualSense back before removing anything.
            if (HidHideCli.Locate() is { } cli)
                new DeviceHider(new HidHideCli(cli), InstalledExe).Restore();

            File.Delete(StartMenuShortcut);
            StartupRegistration.Set(false, InstalledExe);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

            // This exe may be the one being removed, so delete the folder from a helper once we exit.
            var psi = new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rd /s /q \"{InstallDir}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
            };
            Process.Start(psi)?.Dispose();

            if (!quiet)
                MessageBox.Show("Joyy was removed. Your settings stay in %APPDATA%\\Joyy in case you reinstall.",
                    "Joyy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            if (!quiet)
                MessageBox.Show($"Could not uninstall Joyy:\n\n{ex.Message}", "Joyy", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>Asks a running Joyy to exit (which also gives the DualSense back) and waits for it.</summary>
    public static bool QuitRunning(int waitSeconds = 12)
    {
        if (!EventWaitHandle.TryOpenExisting(Program.QuitEventName, out var quit))
            return true;
        using (quit)
            quit.Set();

        var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
        while (DateTime.UtcNow < deadline)
        {
            using var probe = new Mutex(true, Program.InstanceMutexName, out bool free);
            if (free)
            {
                probe.ReleaseMutex();
                return true;
            }
            Thread.Sleep(200);
        }
        return false;
    }

    static void CopyFiles(string source)
    {
        string sourceDir = Path.GetDirectoryName(source)!;
        Directory.CreateDirectory(InstallDir);

        // A single-file build is just Joyy.exe. A plain build (dotnet build output) also needs the
        // assemblies and config files next to it, but only copy those when Joyy.dll proves that
        // is what this folder is, so we never sweep up an unrelated Downloads folder.
        var files = new List<string> { source };
        if (File.Exists(Path.Combine(sourceDir, "Joyy.dll")))
        {
            foreach (var f in Directory.EnumerateFiles(sourceDir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".dll" or ".json" or ".pdb")
                    files.Add(f);
            }
        }

        // The guardian (and a Joyy that is still shutting down) keep the installed exe locked for
        // a moment after the quit signal; wait for them, then retry briefly if Windows still holds it.
        WaitForInstalledProcesses();
        foreach (var f in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string target = Path.Combine(InstallDir, Path.GetFileName(f));
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Copy(f, target, overwrite: true);
                    break;
                }
                catch (IOException) when (attempt < 25)
                {
                    Thread.Sleep(200);
                }
            }
        }

        // License of the bundled HidHide installer, next to the exe.
        using var notices = typeof(Installer).Assembly.GetManifestResourceStream("ThirdPartyNotices.txt");
        if (notices is not null)
        {
            using var file = File.Create(Path.Combine(InstallDir, "THIRD-PARTY-NOTICES.txt"));
            notices.CopyTo(file);
        }
    }

    /// <summary>Waits up to 10 s for Joyy processes started from the install folder (app or guardian) to exit.</summary>
    static void WaitForInstalledProcesses()
    {
        int self = Environment.ProcessId;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        foreach (var process in Process.GetProcessesByName("Joyy"))
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || !string.Equals(process.MainModule?.FileName, InstalledExe, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var left = deadline - DateTime.UtcNow;
                    if (left > TimeSpan.Zero)
                        process.WaitForExit(left);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Exited meanwhile, or not ours to inspect.
                }
            }
        }
    }

    static void CreateShortcut()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StartMenuShortcut)!);
        var shell = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is not available.");
        dynamic wsh = Activator.CreateInstance(shell)!;
        dynamic link = wsh.CreateShortcut(StartMenuShortcut);
        link.TargetPath = InstalledExe;
        link.WorkingDirectory = InstallDir;
        link.Description = "Joyy: use your DualSense as an Xbox 360 controller";
        link.IconLocation = InstalledExe + ",0";
        link.Save();
    }

    static void RegisterUninstall()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        string version = typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        long size = new FileInfo(InstalledExe).Length / 1024;
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "Joyy");
        key.SetValue("DisplayIcon", InstalledExe);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
        key.SetValue("EstimatedSize", (int)Math.Min(size, int.MaxValue), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
}
