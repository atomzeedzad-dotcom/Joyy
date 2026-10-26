using Joyy.Core;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Joyy.Platform;

/// <summary>Virtual Xbox 360 controller on the ViGEmBus driver.</summary>
public sealed class ViGEmPad : IVirtualPad
{
    static readonly (X360Buttons Flag, Xbox360Button Button)[] ButtonTable =
    {
        (X360Buttons.DpadUp, Xbox360Button.Up),
        (X360Buttons.DpadDown, Xbox360Button.Down),
        (X360Buttons.DpadLeft, Xbox360Button.Left),
        (X360Buttons.DpadRight, Xbox360Button.Right),
        (X360Buttons.Start, Xbox360Button.Start),
        (X360Buttons.Back, Xbox360Button.Back),
        (X360Buttons.LeftThumb, Xbox360Button.LeftThumb),
        (X360Buttons.RightThumb, Xbox360Button.RightThumb),
        (X360Buttons.LeftShoulder, Xbox360Button.LeftShoulder),
        (X360Buttons.RightShoulder, Xbox360Button.RightShoulder),
        (X360Buttons.Guide, Xbox360Button.Guide),
        (X360Buttons.A, Xbox360Button.A),
        (X360Buttons.B, Xbox360Button.B),
        (X360Buttons.X, Xbox360Button.X),
        (X360Buttons.Y, Xbox360Button.Y),
    };

    readonly ViGEmClient _client;
    readonly IXbox360Controller _pad;
    bool _connected;

    /// <summary>Opens the bus. Throws a Nefarius.ViGEm.Client exception if ViGEmBus is missing.</summary>
    public ViGEmPad()
    {
        _client = new ViGEmClient();
        _pad = _client.CreateXbox360Controller();
        _pad.AutoSubmitReport = false;
        _pad.FeedbackReceived += OnFeedback;
    }

    public event Action<byte, byte>? Rumble;

    public int? UserIndex
    {
        get
        {
            if (!_connected)
                return null;
            try { return _pad.UserIndex; }
            catch (Exception) { return null; } // not reported by the driver yet
        }
    }

    public void Connect()
    {
        _pad.Connect();
        _connected = true;

        // ViGEmBus drops a report that equals its zero-initialized "previous" report, so a fresh
        // pad that is only ever sent neutral never delivers an input report, and XInput shows
        // uninitialized stick values (seen here: L(-3356,-1869) R(-3255,-848)). Send one
        // imperceptibly different report first so the neutral one that follows goes through.
        Submit(X360Report.Neutral with { ThumbLX = 1 });
        Submit(X360Report.Neutral);
    }

    public void Submit(in X360Report report)
    {
        foreach (var (flag, button) in ButtonTable)
            _pad.SetButtonState(button, (report.Buttons & flag) != 0);

        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, report.ThumbLX);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, report.ThumbLY);
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, report.ThumbRX);
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, report.ThumbRY);
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, report.LeftTrigger);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, report.RightTrigger);
        _pad.SubmitReport();
    }

    public void Dispose()
    {
        _pad.FeedbackReceived -= OnFeedback;
        if (_connected)
        {
            try { _pad.Disconnect(); }
            catch (Exception) { /* bus already gone; nothing left to unplug */ }
            _connected = false;
        }
        _client.Dispose();
    }

    void OnFeedback(object? sender, Xbox360FeedbackReceivedEventArgs e) => Rumble?.Invoke(e.LargeMotor, e.SmallMotor);
}
