namespace Joyy.Core;

/// <summary>Xbox 360 buttons. Values match XINPUT_GAMEPAD wButtons (Guide = ViGEm's 0x0400).</summary>
[Flags]
public enum X360Buttons : ushort
{
    None = 0,
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

/// <summary>Full state submitted to the virtual Xbox 360 pad. Sticks use the XInput convention (Y up).</summary>
public readonly record struct X360Report(
    X360Buttons Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short ThumbLX,
    short ThumbLY,
    short ThumbRX,
    short ThumbRY)
{
    public static readonly X360Report Neutral = default;

    public bool IsPressed(X360Buttons button) => (Buttons & button) == button;
}
