using Joyy.Core;

namespace Joyy.Platform;

public enum HostEventKind
{
    Info,
    Warning,
    Error,
}

public sealed record HostEvent(HostEventKind Kind, string Message);

/// <summary>Thread-safe copy of the bridge state for the UI.</summary>
public readonly record struct BridgeSnapshot(
    bool Running,
    BridgeStatus Status,
    string? Detail,
    PadInfo? Device,
    int? VirtualSlot,
    PadState Input,
    X360Report Output);

/// <summary>
/// Runs the <see cref="Bridge"/> on its own input thread (SDL must be created, polled and
/// destroyed on one thread). Start/Stop may be called from the UI thread; events are raised
/// on the input thread, so UI code must marshal them.
/// </summary>
public sealed class BridgeHost : IDisposable
{
    readonly object _gate = new();
    readonly Func<IVirtualPad> _createVirtual;
    Thread? _thread;
    CancellationTokenSource? _cancel;
    Mapper _mapper;
    volatile Bridge? _bridge;
    BridgeSnapshot _snapshot;

    public BridgeHost(Profile profile, Func<IVirtualPad>? createVirtual = null)
    {
        _mapper = new Mapper(profile);
        _createVirtual = createVirtual ?? (() => new ViGEmPad());
    }

    public event Action<HostEvent>? Event;
    public event Action<BridgeStatus, string?>? StatusChanged;

    public bool IsRunning => _thread is { IsAlive: true };

    public Profile ActiveProfile => Volatile.Read(ref _mapper).Profile;

    public BridgeSnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    /// <summary>Switches the mapping; takes effect on the next input tick. Throws if the profile is invalid.</summary>
    public void SetProfile(Profile profile)
    {
        var mapper = new Mapper(profile);
        Volatile.Write(ref _mapper, mapper);
        if (_bridge is { } bridge)
            bridge.Mapper = mapper;
    }

    public void Start()
    {
        if (IsRunning)
            return;
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        _thread = new Thread(() => Loop(token))
        {
            Name = "Joyy input",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    /// <summary>Stops the loop and waits for the virtual controller to be unplugged.</summary>
    public void Stop()
    {
        if (_thread is null)
            return;
        _cancel?.Cancel();
        if (!_thread.Join(TimeSpan.FromSeconds(3)))
            Raise(HostEventKind.Warning, "Input thread did not stop within 3 s.");
        _thread = null;
        _cancel?.Dispose();
        _cancel = null;
    }

    public void Dispose() => Stop();

    void Loop(CancellationToken cancel)
    {
        try
        {
            using var timer = new TimerResolution(1);
            var rescanDelay = MinRescanDelay;
            bool first = true;
            while (!cancel.IsCancellationRequested && RunSession(cancel, first, ref rescanDelay))
                first = false; // the watchdog asked for a fresh SDL; go round again
        }
        catch (Exception ex)
        {
            Raise(HostEventKind.Error, $"Input loop failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _bridge = null;
            lock (_gate)
                _snapshot = _snapshot with { Running = false, Device = null, VirtualSlot = null,
                    Input = PadState.Neutral, Output = X360Report.Neutral };
            Raise(HostEventKind.Info, "Stopped; virtual controller unplugged");
        }
    }

    static readonly TimeSpan MinRescanDelay = TimeSpan.FromSeconds(3);
    static readonly TimeSpan MaxRescanDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// One SDL lifetime. Returns true when SDL should be restarted: Windows reports a DualSense
    /// but SDL has not picked it up (seen after a replug while HidHide hides the device).
    /// </summary>
    bool RunSession(CancellationToken cancel, bool first, ref TimeSpan rescanDelay)
    {
        {
            using var pad = new SdlPad();
            pad.Connected += d => Raise(HostEventKind.Info,
                $"DualSense connected via {(d.Connection == ConnectionKind.Usb ? "USB" : d.Connection.ToString())} (serial {d.Serial ?? "unknown"})");
            pad.Disconnected += _ => Raise(HostEventKind.Info, "DualSense disconnected");
            pad.Warning += w => Raise(HostEventKind.Warning, w);

            using var bridge = new Bridge(pad, _createVirtual, Volatile.Read(ref _mapper));
            _bridge = bridge;
            bridge.Mapper = Volatile.Read(ref _mapper); // in case SetProfile raced the line above

            int? slot = null;
            long nextSlotQuery = 0;
            string? detailText = null;
            bridge.StatusChanged += (status, detail) =>
            {
                slot = null;
                nextSlotQuery = 0;
                detailText = detail;
                Raise(status == BridgeStatus.VirtualPadUnavailable ? HostEventKind.Error : HostEventKind.Info,
                    status switch
                    {
                        BridgeStatus.Running => "Virtual Xbox 360 controller plugged in",
                        BridgeStatus.WaitingForController => "Waiting for a DualSense",
                        _ => $"Virtual controller unavailable: {detail}",
                    });
                StatusChanged?.Invoke(status, detail);
            };

            if (first)
                Raise(HostEventKind.Info, $"Started (SDL {SdlPad.NativeVersion})");

            long waitingSince = Environment.TickCount64;
            long nextPresenceCheck = waitingSince + (long)rescanDelay.TotalMilliseconds;

            while (!cancel.IsCancellationRequested)
            {
                bridge.Tick();

                // Watchdog: no controller in SDL, but Windows has one -> start SDL fresh.
                long now = Environment.TickCount64;
                if (pad.Device is not null)
                {
                    waitingSince = now;
                    nextPresenceCheck = now + (long)MinRescanDelay.TotalMilliseconds;
                    rescanDelay = MinRescanDelay;
                }
                else if (now >= nextPresenceCheck)
                {
                    nextPresenceCheck = now + (long)rescanDelay.TotalMilliseconds;
                    if (DevicePresence.AnyDualSensePresent())
                    {
                        Raise(HostEventKind.Info, "A DualSense is plugged in but was not picked up; reconnecting");
                        rescanDelay = TimeSpan.FromTicks(Math.Min(rescanDelay.Ticks * 2, MaxRescanDelay.Ticks));
                        return true;
                    }
                }

                // UserIndex is a driver round-trip; ask about once a second until known.
                if (slot is null && bridge.VirtualPad is { } v && Environment.TickCount64 >= nextSlotQuery)
                {
                    slot = v.UserIndex;
                    nextSlotQuery = Environment.TickCount64 + 1000;
                }

                var snapshot = new BridgeSnapshot(true, bridge.Status, detailText, pad.Device, slot,
                    bridge.LastInput, bridge.LastOutput);
                lock (_gate)
                    _snapshot = snapshot;

                Thread.Sleep(1);
            }
            _bridge = null;
            return false;
        }
    }

    void Raise(HostEventKind kind, string message) => Event?.Invoke(new HostEvent(kind, message));
}
