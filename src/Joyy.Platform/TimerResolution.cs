using System.Runtime.InteropServices;

namespace Joyy.Platform;

/// <summary>
/// Raises the Windows timer resolution while alive so Thread.Sleep(1) sleeps ~1 ms
/// instead of ~15.6 ms. Restored on dispose.
/// </summary>
public sealed partial class TimerResolution : IDisposable
{
    readonly uint _periodMs;
    bool _active;

    public TimerResolution(uint periodMs = 1)
    {
        _periodMs = periodMs;
        KeepTimerWhenHidden();
        _active = timeBeginPeriod(periodMs) == 0;
    }

    /// <summary>
    /// Windows 11 ignores timeBeginPeriod for a process whose windows are hidden or covered, which
    /// is exactly how Joyy runs during a game (in the tray): Sleep(1) then takes ~5 ms and polling
    /// drops from ~500 to ~180 Hz. Opt out of that, and of EcoQoS slow-down, for this process.
    /// </summary>
    static void KeepTimerWhenHidden()
    {
        var state = new PowerThrottlingState
        {
            Version = 1,
            ControlMask = ThrottleExecutionSpeed | ThrottleIgnoreTimerResolution,
            StateMask = 0, // 0 = these throttles are off
        };
        // Not available before Windows 10 1709 / 11; failure just keeps the old behavior.
        _ = SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
    }

    const int ProcessPowerThrottling = 4;
    const uint ThrottleExecutionSpeed = 0x1;
    const uint ThrottleIgnoreTimerResolution = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(nint process, int informationClass, ref PowerThrottlingState information, int size);

    public void Dispose()
    {
        if (_active)
            timeEndPeriod(_periodMs);
        _active = false;
    }

    [LibraryImport("winmm.dll")]
    private static partial uint timeBeginPeriod(uint uPeriod);

    [LibraryImport("winmm.dll")]
    private static partial uint timeEndPeriod(uint uPeriod);
}
