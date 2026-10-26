using System.Diagnostics;
using Joyy.Platform;

namespace Joyy.App;

internal static class Program
{
    public const string InstanceMutexName = @"Local\Joyy.App.SingleInstance";
    public const string ShowEventName = @"Local\Joyy.App.Show";
    public const string QuitEventName = @"Local\Joyy.App.Quit";

    static bool Has(string[] args, string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    [STAThread]
    static int Main(string[] args)
    {
        // Commands that must not become "the" running Joyy.
        if (args.Length == 2 && string.Equals(args[0], "--guardian", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[1], out int watched))
            return RunGuardian(watched);
        if (Has(args, "--restore"))
            return RestoreVisibility();
        if (Has(args, "--quit"))
            return Installer.QuitRunning() ? 0 : 1;
        if (Has(args, "--install"))
        {
            ApplicationConfiguration.Initialize();
            // Starting with Windows only happens when asked for (--autostart); --no-autostart removes it.
            bool? autostart = Has(args, "--autostart") ? true : Has(args, "--no-autostart") ? false : null;
            return Installer.Install(launch: !Has(args, "--no-launch"), quiet: Has(args, "--quiet"), autostart);
        }
        if (Has(args, "--uninstall"))
        {
            ApplicationConfiguration.Initialize();
            return Installer.Uninstall(quiet: Has(args, "--quiet"));
        }
        if (args.Length == 2 && string.Equals(args[0], "--render-preview", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            return PreviewRenderer.Run(args[1]);
        }

        // One Joyy per user session. A second launch just asks the first to show its window.
        using var instance = new Mutex(true, InstanceMutexName, out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!first)
        {
            showSignal.Set();
            return 0;
        }
        using var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);

        ApplicationConfiguration.Initialize();

#if PUBLISHED
        // A downloaded Joyy.exe that is not installed yet: offer to install it so it starts with
        // Windows and can be found from the Start menu.
        if (!Installer.RunningFromInstallDir && !Has(args, "--minimized") && OfferInstall() is { } startWithWindows)
        {
            instance.ReleaseMutex();
            return Installer.Install(launch: true, quiet: false, startWithWindows);
        }
#endif

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show($"Joyy hit an unexpected error:\n\n{e.Exception.Message}", "Joyy",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

        bool hidden = Has(args, "--minimized");
        Application.Run(new TrayApp(hidden, showSignal, quitSignal));
        return 0;
    }

#if PUBLISHED
    /// <summary>Asks whether to install: null = run once without installing, otherwise whether to also start with Windows.</summary>
    static bool? OfferInstall()
    {
        string already = Installer.IsInstalled ? "update the installed copy of" : "install";
        var answer = MessageBox.Show(
            $"Do you want to {already} Joyy?\n\n" +
            "• It appears in the Start menu: type \"Joyy\" to find it\n" +
            "• It can be removed in Windows Settings > Apps\n\n" +
            "Choose No to just run it this once.",
            "Joyy", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return null;

        var start = MessageBox.Show(
            "Start Joyy automatically when you sign in to Windows?\n\n" +
            "Recommended: your DualSense only works as an Xbox controller while Joyy is running, " +
            "so with this on you never have to remember to open it.\n\n" +
            "You can change this later in Joyy's Settings.",
            "Joyy", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
        return start == DialogResult.Yes;
    }
#endif

    /// <summary>
    /// Safety net, run as "Joyy.exe --guardian PID": waits for Joyy to exit, and if it left the
    /// DualSense hidden (crash, End task, power loss) gives it back at once, instead of waiting
    /// for the next start. Does nothing when Joyy already cleaned up.
    /// </summary>
    static int RunGuardian(int pid)
    {
        try
        {
            using var joyy = Process.GetProcessById(pid);
            joyy.WaitForExit();
        }
        catch (ArgumentException)
        {
            // Already gone.
        }

        if (HidHideCli.Locate() is not { } cli)
            return 0;
        var hider = new DeviceHider(new HidHideCli(cli), Environment.ProcessPath!);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                hider.RestoreIfOwnedBy(pid);
                return 0;
            }
            catch (Exception)
            {
                Thread.Sleep(1000); // HidHide may be busy; the journal keeps the work pending
            }
        }
        return 1;
    }

    /// <summary>Emergency fix: "Joyy.exe --restore" makes the DualSense visible again without starting the app.</summary>
    static int RestoreVisibility()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (HidHideCli.Locate() is not { } cli)
            {
                MessageBox.Show("HidHide is not installed, so Joyy never hid anything.", "Joyy");
                return 0;
            }
            var hider = new DeviceHider(new HidHideCli(cli), Environment.ProcessPath!);
            bool had = hider.HasPendingRestore;
            hider.Restore();
            MessageBox.Show(had ? "Your DualSense is visible to games again." : "Nothing to restore: Joyy has nothing hidden.", "Joyy");
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not restore: {ex.Message}", "Joyy", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
