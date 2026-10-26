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
        _active = timeBeginPeriod(periodMs) == 0;
    }

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
