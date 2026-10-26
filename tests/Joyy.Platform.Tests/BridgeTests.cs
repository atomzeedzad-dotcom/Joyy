using Joyy.Core;
using Joyy.Platform;

namespace Joyy.Platform.Tests;

public class BridgeTests
{
    readonly FakeTime _time = new();
    readonly FakePhysical _physical = new();
    readonly List<FakeVirtual> _created = new();
    readonly List<BridgeStatus> _statuses = new();
    Func<FakeVirtual> _factory = () => new FakeVirtual();

    Bridge NewBridge(Profile? profile = null)
    {
        var bridge = new Bridge(_physical, () =>
        {
            var v = _factory();
            _created.Add(v);
            return v;
        }, new Mapper(profile ?? new Profile()), _time, TimeSpan.FromMilliseconds(100));
        bridge.StatusChanged += (s, _) => _statuses.Add(s);
        return bridge;
    }

    static PadState Pressing(PhysicalButton b) => PadState.Neutral with { Buttons = PhysicalButtons.Bit(b) };

    [Fact]
    public void No_controller_means_no_virtual_pad()
    {
        var bridge = NewBridge();
        bridge.Tick();
        bridge.Tick();
        Assert.Empty(_created);
        Assert.Equal(BridgeStatus.WaitingForController, bridge.Status);
    }

    [Fact]
    public void Connecting_creates_one_virtual_pad_and_submits_mapped_report()
    {
        var bridge = NewBridge();
        _physical.IsConnected = true;
        _physical.State = Pressing(PhysicalButton.Cross);

        bridge.Tick();
        bridge.Tick();

        var pad = Assert.Single(_created);
        Assert.True(pad.Connected);
        Assert.Equal(X360Buttons.A, Assert.Single(pad.Submitted).Buttons);
        Assert.Equal(BridgeStatus.Running, bridge.Status);
    }

    [Fact]
    public void Unchanged_report_is_resent_only_after_keep_alive()
    {
        var bridge = NewBridge();
        _physical.IsConnected = true;
        bridge.Tick();
        var pad = _created[0];

        _time.Advance(TimeSpan.FromMilliseconds(50));
        bridge.Tick();
        Assert.Single(pad.Submitted);

        _time.Advance(TimeSpan.FromMilliseconds(60));
        bridge.Tick();
        Assert.Equal(2, pad.Submitted.Count);
    }

    [Fact]
    public void Changed_report_is_sent_immediately()
    {
        var bridge = NewBridge();
        _physical.IsConnected = true;
        bridge.Tick();
        _physical.State = Pressing(PhysicalButton.R1);
        bridge.Tick();

        var pad = _created[0];
        Assert.Equal(2, pad.Submitted.Count);
        Assert.Equal(X360Buttons.RightShoulder, pad.Submitted[1].Buttons);
    }

    [Fact]
    public void Disconnecting_unplugs_the_virtual_pad_and_stops_rumble()
    {
        var bridge = NewBridge();
        _physical.IsConnected = true;
        bridge.Tick();
        _created[0].RaiseRumble(255, 255);
        Assert.NotEqual((ushort)0, _physical.LastRumble.Low);

        _physical.IsConnected = false;
        bridge.Tick();

        Assert.True(_created[0].Disposed);
        Assert.False(_created[0].HasRumbleSubscriber);
        Assert.Equal(((ushort)0, (ushort)0), _physical.LastRumble);
        Assert.Null(bridge.VirtualPad);
        Assert.Equal(BridgeStatus.WaitingForController, bridge.Status);
    }

    [Fact]
    public void Reconnecting_creates_a_fresh_virtual_pad()
    {
        var bridge = NewBridge();
        _physical.IsConnected = true;
        bridge.Tick();
        _physical.IsConnected = false;
        bridge.Tick();
        _physical.IsConnected = true;
        bridge.Tick();

        Assert.Equal(2, _created.Count);
        Assert.Single(_created[1].Submitted);
    }

    [Fact]
    public void Bus_failure_backs_off_exponentially_then_recovers()
    {
        _factory = () => new FakeVirtual { ConnectThrows = true };
        var bridge = NewBridge();
        _physical.IsConnected = true;

        bridge.Tick();
        Assert.Single(_created);
        Assert.True(_created[0].Disposed);
        Assert.Equal(BridgeStatus.VirtualPadUnavailable, bridge.Status);

        _time.Advance(TimeSpan.FromMilliseconds(900));
        bridge.Tick();
        Assert.Single(_created);            // 1 s backoff not over

        _time.Advance(TimeSpan.FromMilliseconds(200));
        bridge.Tick();
        Assert.Equal(2, _created.Count);   // retried after 1 s

        _time.Advance(TimeSpan.FromMilliseconds(1500));
        bridge.Tick();
        Assert.Equal(2, _created.Count);   // now 2 s backoff

        _factory = () => new FakeVirtual();
        _time.Advance(TimeSpan.FromMilliseconds(600));
        bridge.Tick();
        Assert.Equal(3, _created.Count);
        Assert.Equal(BridgeStatus.Running, bridge.Status);
        Assert.Single(_created[2].Submitted);
    }

    [Fact]
    public void Backoff_is_capped()
    {
        _factory = () => new FakeVirtual { ConnectThrows = true };
        var bridge = NewBridge();
        _physical.IsConnected = true;

        for (int i = 0; i < 12; i++)
        {
            bridge.Tick();
            _time.Advance(Bridge.MaxRetryDelay + TimeSpan.FromMilliseconds(1));
        }
        Assert.Equal(12, _created.Count); // every attempt waited at most MaxRetryDelay
    }

    [Fact]
    public void Submit_failure_drops_the_pad_and_retries_later()
    {
        var bridge = NewBridge();
        _physical.IsConnected = true;
        bridge.Tick();
        _created[0].SubmitThrows = true;
        _physical.State = Pressing(PhysicalButton.Cross);

        bridge.Tick();
        Assert.True(_created[0].Disposed);
        Assert.Equal(BridgeStatus.VirtualPadUnavailable, bridge.Status);

        _time.Advance(TimeSpan.FromSeconds(1.1));
        bridge.Tick();
        Assert.Equal(2, _created.Count);
        Assert.Equal(X360Buttons.A, Assert.Single(_created[1].Submitted).Buttons);
    }

    [Fact]
    public void Rumble_is_forwarded_with_profile_scale()
    {
        var bridge = NewBridge(new Profile { RumbleScale = 0.5 });
        _physical.IsConnected = true;
        bridge.Tick();

        _created[0].RaiseRumble(255, 128);
        Assert.Equal((AnalogMath.RumbleToSdl(255, 0.5), AnalogMath.RumbleToSdl(128, 0.5)), _physical.LastRumble);
    }

    [Fact]
    public void Swapping_mapper_takes_effect_on_next_tick()
    {
        var bridge = NewBridge();
        _physical.IsConnected = true;
        _physical.State = Pressing(PhysicalButton.Cross);
        bridge.Tick();

        bridge.Mapper = new Mapper(new Profile { NintendoLayout = true });
        bridge.Tick();

        Assert.Equal(X360Buttons.B, _created[0].Submitted[^1].Buttons);
    }

    [Fact]
    public void Status_events_fire_only_on_change()
    {
        var bridge = NewBridge();
        bridge.Tick();
        bridge.Tick();
        _physical.IsConnected = true;
        bridge.Tick();
        bridge.Tick();

        Assert.Equal(new[] { BridgeStatus.Running }, _statuses);
    }
}
