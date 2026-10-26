using Joyy.Core;

namespace Joyy.Platform;

public enum BridgeStatus
{
    WaitingForController,
    Running,
    VirtualPadUnavailable,
}

/// <summary>
/// The input loop: physical DualSense -> Mapper -> virtual Xbox 360 pad, plus rumble back.
/// The virtual pad exists only while a DualSense is connected, so games never see an idle
/// phantom controller. ViGEm failures are retried with exponential backoff.
/// Call <see cref="Tick"/> from a single thread (the one that owns the physical pad).
/// </summary>
public sealed class Bridge : IDisposable
{
    public static readonly TimeSpan DefaultKeepAlive = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    readonly IPhysicalPad _physical;
    readonly Func<IVirtualPad> _createVirtual;
    readonly TimeProvider _time;
    readonly TimeSpan _keepAlive;

    Mapper _mapper;
    IVirtualPad? _virtual;
    X360Report _lastSent;
    long _lastSentAt;
    bool _sentSinceConnect;
    TimeSpan _retryDelay = MinRetryDelay;
    long _nextAttemptAt;
    bool _retryPending;
    string? _statusDetail;

    public Bridge(IPhysicalPad physical, Func<IVirtualPad> createVirtual, Mapper mapper,
        TimeProvider? time = null, TimeSpan? keepAlive = null)
    {
        _physical = physical;
        _createVirtual = createVirtual;
        _mapper = mapper;
        _time = time ?? TimeProvider.System;
        _keepAlive = keepAlive ?? DefaultKeepAlive;
    }

    /// <summary>Current mapping. May be replaced from any thread (profile switch or hot reload).</summary>
    public Mapper Mapper
    {
        get => Volatile.Read(ref _mapper);
        set => Volatile.Write(ref _mapper, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public BridgeStatus Status { get; private set; } = BridgeStatus.WaitingForController;
    public IVirtualPad? VirtualPad => _virtual;
    public PadState LastInput { get; private set; }
    public X360Report LastOutput { get; private set; }

    /// <summary>Raised on the Tick thread when the status or its detail text changes.</summary>
    public event Action<BridgeStatus, string?>? StatusChanged;

    /// <summary>Runs <see cref="Tick"/> about every millisecond until cancelled.</summary>
    public void Run(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            Tick();
            Thread.Sleep(1);
        }
    }

    public void Tick()
    {
        bool connected = _physical.Poll(out var state);
        LastInput = state;

        if (!connected)
        {
            DropVirtual();
            LastOutput = X360Report.Neutral;
            SetStatus(BridgeStatus.WaitingForController, null);
            return;
        }

        if (_virtual is null && !TryCreateVirtual())
            return;

        var report = Mapper.Map(state);
        LastOutput = report;

        long now = _time.GetTimestamp();
        if (_sentSinceConnect && report == _lastSent && _time.GetElapsedTime(_lastSentAt, now) < _keepAlive)
            return;

        try
        {
            _virtual!.Submit(report);
            _lastSent = report;
            _lastSentAt = now;
            _sentSinceConnect = true;
        }
        catch (Exception ex)
        {
            DropVirtual();
            ScheduleRetry(ex);
        }
    }

    public void Dispose() => DropVirtual();

    bool TryCreateVirtual()
    {
        if (_retryPending && _time.GetTimestamp() < _nextAttemptAt)
            return false;

        IVirtualPad? pad = null;
        try
        {
            pad = _createVirtual();
            pad.Rumble += OnRumble;
            pad.Connect();
        }
        catch (Exception ex)
        {
            if (pad is not null)
            {
                pad.Rumble -= OnRumble;
                TryDispose(pad);
            }
            ScheduleRetry(ex);
            return false;
        }

        _virtual = pad;
        _sentSinceConnect = false;
        _retryPending = false;
        _retryDelay = MinRetryDelay;
        SetStatus(BridgeStatus.Running, null);
        return true;
    }

    void ScheduleRetry(Exception ex)
    {
        long now = _time.GetTimestamp();
        _nextAttemptAt = now + (long)(_retryDelay.TotalSeconds * _time.TimestampFrequency);
        _retryPending = true;
        SetStatus(BridgeStatus.VirtualPadUnavailable,
            $"{ex.GetType().Name}: {ex.Message} (retrying in {_retryDelay.TotalSeconds:0}s)");
        _retryDelay = TimeSpan.FromTicks(Math.Min(_retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
    }

    void DropVirtual()
    {
        if (_virtual is null)
            return;

        _virtual.Rumble -= OnRumble;
        TryDispose(_virtual);
        _virtual = null;
        _sentSinceConnect = false;
        _physical.SetRumble(0, 0);
    }

    // Called on a ViGEm driver thread; IPhysicalPad.SetRumble is thread-safe.
    void OnRumble(byte large, byte small)
    {
        double scale = Mapper.Profile.RumbleScale;
        _physical.SetRumble(AnalogMath.RumbleToSdl(large, scale), AnalogMath.RumbleToSdl(small, scale));
    }

    void SetStatus(BridgeStatus status, string? detail)
    {
        if (status == Status && detail == _statusDetail)
            return;
        Status = status;
        _statusDetail = detail;
        StatusChanged?.Invoke(status, detail);
    }

    static void TryDispose(IVirtualPad pad)
    {
        try { pad.Dispose(); }
        catch (Exception) { /* the bus may already be gone */ }
    }
}
