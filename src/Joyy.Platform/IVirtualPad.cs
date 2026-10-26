using Joyy.Core;

namespace Joyy.Platform;

/// <summary>A virtual Xbox 360 controller as games see it. Dispose unplugs it.</summary>
public interface IVirtualPad : IDisposable
{
    /// <summary>Plugs the controller in. Throws if the bus driver is unavailable.</summary>
    void Connect();

    /// <summary>Sends a full report. Throws if the bus driver went away.</summary>
    void Submit(in X360Report report);

    /// <summary>XInput slot (0..3) Windows assigned, or null if not known yet.</summary>
    int? UserIndex { get; }

    /// <summary>Raised from a driver thread when a game sets rumble: (large motor, small motor), 0..255.</summary>
    event Action<byte, byte>? Rumble;
}
