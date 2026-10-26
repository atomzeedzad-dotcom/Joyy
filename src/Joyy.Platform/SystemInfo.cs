using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Joyy.Platform;

/// <summary>Which drivers Joyy depends on are installed.</summary>
public static class DependencyChecker
{
    public const string ViGEmBusUrl = "https://github.com/nefarius/ViGEmBus/releases";
    public const string HidHideUrl = "https://github.com/nefarius/HidHide/releases";

    public static bool IsViGEmBusInstalled => ServiceExists("ViGEmBus");
    public static bool IsHidHideInstalled => ServiceExists("HidHide");

    static bool ServiceExists(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        return key is not null;
    }
}

/// <summary>Other controller tools that can also grab the DualSense and cause duplicate controllers.</summary>
public static class ConflictDetector
{
    static readonly (string Process, string Name)[] Known =
    {
        ("DS4Windows", "DS4Windows"),
        ("DSX", "DSX / DualSenseX"),
        ("DualSenseX", "DSX / DualSenseX"),
        ("steam", "Steam (Steam Input)"),
        ("reWASDEngine", "reWASD"),
        ("x360ce", "x360ce"),
    };

    public static IReadOnlyList<string> RunningConflicts()
    {
        var found = new List<string>();
        foreach (var (process, name) in Known)
        {
            var procs = Process.GetProcessesByName(process);
            if (procs.Length > 0 && !found.Contains(name))
                found.Add(name);
            foreach (var p in procs)
                p.Dispose();
        }
        return found;
    }
}

/// <summary>Name of the process that owns the foreground window (for per-game profiles).</summary>
public static partial class ForegroundProcess
{
    public static string? GetName()
    {
        nint hwnd = GetForegroundWindow();
        if (hwnd == 0 || GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0)
            return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.Id == Environment.ProcessId ? null : p.ProcessName;
        }
        catch (ArgumentException) { return null; } // exited
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);
}

/// <summary>Per-user "start with Windows" entry under HKCU\...\Run.</summary>
public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Joyy";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{exePath}\" --minimized");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
