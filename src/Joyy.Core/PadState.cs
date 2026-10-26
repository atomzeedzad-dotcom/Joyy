namespace Joyy.Core;

/// <summary>
/// One snapshot of the physical DualSense, already decoded by the platform layer.
/// </summary>
/// <remarks>
/// Conventions (the platform layer must convert to these):
/// <list type="bullet">
/// <item>Sticks are -32768..32767, X positive = right, Y positive = UP (XInput convention;
/// SDL reports Y positive = down, so SdlPad must flip it).</item>
/// <item>Triggers are 0 (released) .. 255 (fully pressed).</item>
/// <item>D-pad is carried as the four Dpad* bits in <see cref="Buttons"/>; use
/// <see cref="HatConverter"/> when the source reports a hat.</item>
/// </list>
/// </remarks>
public readonly record struct PadState(
    uint Buttons,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY,
    byte L2,
    byte R2)
{
    public static readonly PadState Neutral = default;

    public bool IsPressed(PhysicalButton button) => (Buttons & PhysicalButtons.Bit(button)) != 0;

    public PadState With(PhysicalButton button, bool pressed) => this with
    {
        Buttons = pressed
            ? Buttons | PhysicalButtons.Bit(button)
            : Buttons & ~PhysicalButtons.Bit(button),
    };
}
