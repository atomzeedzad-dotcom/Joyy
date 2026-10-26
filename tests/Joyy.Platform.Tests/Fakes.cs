using Joyy.Core;
using Joyy.Platform;

namespace Joyy.Platform.Tests;

sealed class FakeTime : TimeProvider
{
    long _now = 1_000_000;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _now;
    public void Advance(TimeSpan by) => _now += by.Ticks;
}

sealed class FakePhysical : IPhysicalPad
{
    public bool IsConnected { get; set; }
    public PadState State { get; set; }
    public (ushort Low, ushort High) LastRumble { get; private set; }
    public PadInfo? Device => IsConnected ? new PadInfo(1, "Fake", 0x054C, 0x0CE6, "s", null, ConnectionKind.Usb) : null;

#pragma warning disable CS0067 // events required by the interface; unused in these tests
    public event Action<PadInfo>? Connected;
    public event Action<PadInfo>? Disconnected;
    public event Action<string>? Warning;
#pragma warning restore CS0067

    public bool Poll(out PadState state)
    {
        state = IsConnected ? State : PadState.Neutral;
        return IsConnected;
    }

    public void SetRumble(ushort lowFrequency, ushort highFrequency) => LastRumble = (lowFrequency, highFrequency);
    public void Dispose() { }
}

sealed class FakeVirtual : IVirtualPad
{
    public List<X360Report> Submitted { get; } = new();
    public bool ConnectThrows { get; set; }
    public bool SubmitThrows { get; set; }
    public bool Connected { get; private set; }
    public bool Disposed { get; private set; }
    public int? UserIndex => Connected ? 0 : null;
    public event Action<byte, byte>? Rumble;

    public void Connect()
    {
        if (ConnectThrows)
            throw new InvalidOperationException("bus not found");
        Connected = true;
    }

    public void Submit(in X360Report report)
    {
        if (SubmitThrows)
            throw new InvalidOperationException("bus gone");
        Submitted.Add(report);
    }

    public void RaiseRumble(byte large, byte small) => Rumble?.Invoke(large, small);
    public bool HasRumbleSubscriber => Rumble is not null;
    public void Dispose() => Disposed = true;
}
