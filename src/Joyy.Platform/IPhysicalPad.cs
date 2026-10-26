using Joyy.Core;

namespace Joyy.Platform;

/// <summary>Identity of the physical controller currently in use.</summary>
public sealed record PadInfo(
    int InstanceId,
    string Name,
    ushort VendorId,
    ushort ProductId,
    string? Serial,
    string? DevicePath,
    ConnectionKind Connection);

/// <summary>
/// Source of DualSense input. Implementations are single-threaded: create, poll and
/// dispose on the same thread. Only <see cref="SetRumble"/> may be called from other threads.
/// </summary>
public interface IPhysicalPad : IDisposable
{
    /// <summary>The controller in use, or null while none is connected.</summary>
    PadInfo? Device { get; }

    event Action<PadInfo>? Connected;
    event Action<PadInfo>? Disconnected;
    event Action<string>? Warning;

    /// <summary>
    /// Processes hot-plug events and reads the current state. Returns false (and a neutral
    /// state) when no controller is connected. Does not allocate in steady state.
    /// </summary>
    bool Poll(out PadState state);

    /// <summary>Requests rumble; applied on the next <see cref="Poll"/>. Thread-safe.</summary>
    void SetRumble(ushort lowFrequency, ushort highFrequency);
}
