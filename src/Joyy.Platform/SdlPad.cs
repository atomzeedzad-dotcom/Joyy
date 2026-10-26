using System.Diagnostics;
using Joyy.Core;
using Joyy.Platform.Sdl;

namespace Joyy.Platform;

/// <summary>
/// Reads one DualSense through SDL2's HIDAPI driver. Owns SDL for the whole process, so only
/// one instance may exist at a time.
/// </summary>
public sealed class SdlPad : IPhysicalPad
{
    static readonly (int Sdl, uint Bit)[] ButtonTable =
    {
        (SdlNative.BUTTON_A, PhysicalButtons.Bit(PhysicalButton.Cross)),
        (SdlNative.BUTTON_B, PhysicalButtons.Bit(PhysicalButton.Circle)),
        (SdlNative.BUTTON_X, PhysicalButtons.Bit(PhysicalButton.Square)),
        (SdlNative.BUTTON_Y, PhysicalButtons.Bit(PhysicalButton.Triangle)),
        (SdlNative.BUTTON_LEFTSHOULDER, PhysicalButtons.Bit(PhysicalButton.L1)),
        (SdlNative.BUTTON_RIGHTSHOULDER, PhysicalButtons.Bit(PhysicalButton.R1)),
        (SdlNative.BUTTON_LEFTSTICK, PhysicalButtons.Bit(PhysicalButton.L3)),
        (SdlNative.BUTTON_RIGHTSTICK, PhysicalButtons.Bit(PhysicalButton.R3)),
        (SdlNative.BUTTON_START, PhysicalButtons.Bit(PhysicalButton.Options)),
        (SdlNative.BUTTON_BACK, PhysicalButtons.Bit(PhysicalButton.Create)),
        (SdlNative.BUTTON_GUIDE, PhysicalButtons.Bit(PhysicalButton.PS)),
        (SdlNative.BUTTON_TOUCHPAD, PhysicalButtons.Bit(PhysicalButton.Touchpad)),
        (SdlNative.BUTTON_MISC1, PhysicalButtons.Bit(PhysicalButton.Mute)),
        (SdlNative.BUTTON_DPAD_UP, PhysicalButtons.Bit(PhysicalButton.DpadUp)),
        (SdlNative.BUTTON_DPAD_DOWN, PhysicalButtons.Bit(PhysicalButton.DpadDown)),
        (SdlNative.BUTTON_DPAD_LEFT, PhysicalButtons.Bit(PhysicalButton.DpadLeft)),
        (SdlNative.BUTTON_DPAD_RIGHT, PhysicalButtons.Bit(PhysicalButton.DpadRight)),
    };

    // SDL clamps rumble duration to 0xFFFF ms, so ongoing rumble is re-sent before it expires.
    const uint RumbleDurationMs = 0xFFFF;
    static readonly long RumbleRefreshTicks = Stopwatch.Frequency * 30;

    readonly int _threadId = Environment.CurrentManagedThreadId;
    nint _controller;
    int _instanceId = -1;
    uint _rumbleRequested;
    uint _rumbleApplied;
    long _rumbleSentAt;
    bool _disposed;

    public SdlPad()
    {
        // Must be set before SDL_Init.
        SdlNative.SetHint("SDL_NO_SIGNAL_HANDLERS", "1");               // Ctrl+C stays ours
        SdlNative.SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1"); // the game has focus, not us
        SdlNative.SetHint("SDL_JOYSTICK_HIDAPI", "1");
        SdlNative.SetHint("SDL_JOYSTICK_HIDAPI_PS5", "1");
        SdlNative.SetHint("SDL_JOYSTICK_HIDAPI_PS5_RUMBLE", "1");       // enhanced reports: rumble over Bluetooth
        // Keep SDL away from Xbox devices, which include our own virtual pad.
        SdlNative.SetHint("SDL_XINPUT_ENABLED", "0");
        SdlNative.SetHint("SDL_JOYSTICK_HIDAPI_XBOX", "0");
        SdlNative.SetHint("SDL_JOYSTICK_RAWINPUT", "0");
        SdlNative.SetHint("SDL_JOYSTICK_WGI", "0");
        SdlNative.SetHint("SDL_DIRECTINPUT_ENABLED", "0");

        if (SdlNative.Init(SdlNative.INIT_GAMECONTROLLER | SdlNative.INIT_EVENTS) != 0)
            throw new InvalidOperationException($"SDL_Init failed: {SdlNative.GetError()}");
    }

    /// <summary>Version of the loaded SDL2.dll, e.g. "2.30.8".</summary>
    public static string NativeVersion
    {
        get
        {
            SdlNative.GetVersion(out var v);
            return $"{v.Major}.{v.Minor}.{v.Patch}";
        }
    }

    public PadInfo? Device { get; private set; }

    public event Action<PadInfo>? Connected;
    public event Action<PadInfo>? Disconnected;
    public event Action<string>? Warning;

    public bool Poll(out PadState state)
    {
        CheckThread();

        while (SdlNative.PollEvent(out var e) != 0)
        {
            if (e.Type == SdlNative.CONTROLLERDEVICEADDED)
                OnAdded(e.Which);
            else if (e.Type == SdlNative.CONTROLLERDEVICEREMOVED)
                OnRemoved(e.Which);
        }

        if (_controller == 0)
        {
            state = PadState.Neutral;
            return false;
        }

        ApplyRumble();
        state = Read(_controller);
        return true;
    }

    public void SetRumble(ushort lowFrequency, ushort highFrequency) =>
        Volatile.Write(ref _rumbleRequested, ((uint)lowFrequency << 16) | highFrequency);

    public void Dispose()
    {
        if (_disposed)
            return;
        CheckThread();
        _disposed = true;

        if (_controller != 0)
        {
            SdlNative.GameControllerRumble(_controller, 0, 0, 0);
            SdlNative.GameControllerClose(_controller);
            _controller = 0;
        }
        SdlNative.Quit();
    }

    static PadState Read(nint c)
    {
        uint buttons = 0;
        foreach (var (sdl, bit) in ButtonTable)
        {
            if (SdlNative.GameControllerGetButton(c, sdl) != 0)
                buttons |= bit;
        }

        // SDL reports Y positive = down; PadState uses Y positive = up.
        return new PadState(
            buttons,
            SdlNative.GameControllerGetAxis(c, SdlNative.AXIS_LEFTX),
            AnalogMath.Invert(SdlNative.GameControllerGetAxis(c, SdlNative.AXIS_LEFTY)),
            SdlNative.GameControllerGetAxis(c, SdlNative.AXIS_RIGHTX),
            AnalogMath.Invert(SdlNative.GameControllerGetAxis(c, SdlNative.AXIS_RIGHTY)),
            AnalogMath.Trigger15To8(SdlNative.GameControllerGetAxis(c, SdlNative.AXIS_TRIGGERLEFT)),
            AnalogMath.Trigger15To8(SdlNative.GameControllerGetAxis(c, SdlNative.AXIS_TRIGGERRIGHT)));
    }

    void OnAdded(int deviceIndex)
    {
        if (SdlNative.IsGameController(deviceIndex) == 0)
            return;
        if (!DeviceIds.IsDualSense(SdlNative.JoystickGetDeviceVendor(deviceIndex),
                SdlNative.JoystickGetDeviceProduct(deviceIndex)))
            return;

        nint c = SdlNative.GameControllerOpen(deviceIndex);
        if (c == 0)
        {
            Warning?.Invoke($"Could not open DualSense: {SdlNative.GetError()}");
            return;
        }

        if (_controller != 0)
        {
            // Opening an already-open device just bumps SDL's refcount; closing undoes that.
            if (c != _controller)
            {
                // Same physical pad on a second transport (USB cable plugged in while on Bluetooth),
                // or a second controller. Release 1 supports one controller.
                var other = Describe(c);
                bool sameSerial = other.Serial is { Length: > 0 } && other.Serial == Device?.Serial;
                Warning?.Invoke(sameSerial
                    ? $"Same controller also connected over {other.Connection}; ignoring the duplicate."
                    : "A second DualSense was connected. Joyy supports one controller; ignoring it.");
            }
            SdlNative.GameControllerClose(c);
            return;
        }

        var info = Describe(c);

        _controller = c;
        _instanceId = info.InstanceId;
        _rumbleApplied = 0;
        Device = info;
        Connected?.Invoke(info);
    }

    void OnRemoved(int instanceId)
    {
        if (_controller == 0 || instanceId != _instanceId)
            return;

        SdlNative.GameControllerClose(_controller);
        _controller = 0;
        _instanceId = -1;
        var old = Device!;
        Device = null;
        Disconnected?.Invoke(old);

        // Another transport of the same pad (or a second pad) may still be attached.
        for (int i = 0; i < SdlNative.NumJoysticks() && _controller == 0; i++)
            OnAdded(i);
    }

    void ApplyRumble()
    {
        uint wanted = Volatile.Read(ref _rumbleRequested);
        long now = Stopwatch.GetTimestamp();
        bool changed = wanted != _rumbleApplied;
        bool expiring = wanted != 0 && now - _rumbleSentAt > RumbleRefreshTicks;
        if (!changed && !expiring)
            return;

        SdlNative.GameControllerRumble(_controller, (ushort)(wanted >> 16), (ushort)wanted,
            wanted == 0 ? 0 : RumbleDurationMs);
        _rumbleApplied = wanted;
        _rumbleSentAt = now;
    }

    static PadInfo Describe(nint c)
    {
        string? path = SdlNative.GameControllerPath(c);
        return new PadInfo(
            SdlNative.JoystickInstanceID(SdlNative.GameControllerGetJoystick(c)),
            SdlNative.GameControllerName(c) ?? "DualSense",
            SdlNative.GameControllerGetVendor(c),
            SdlNative.GameControllerGetProduct(c),
            SdlNative.GameControllerGetSerial(c),
            path,
            DeviceIds.ClassifyConnection(path));
    }

    void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("SdlPad must be used from the thread that created it.");
    }
}
