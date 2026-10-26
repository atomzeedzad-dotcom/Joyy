namespace Joyy.Core;

/// <summary>
/// DualSense buttons, named by what is printed on the controller.
/// The numeric value is the bit index inside <see cref="PadState.Buttons"/>.
/// </summary>
public enum PhysicalButton : byte
{
    Cross,
    Circle,
    Square,
    Triangle,
    L1,
    R1,
    L3,
    R3,
    Options,
    Create,
    PS,
    Touchpad,
    Mute,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
}

public static class PhysicalButtons
{
    public static readonly int Count = Enum.GetValues<PhysicalButton>().Length;

    public static uint Bit(PhysicalButton button) => 1u << (int)button;

    public static uint Of(params PhysicalButton[] buttons)
    {
        uint bits = 0;
        foreach (var b in buttons)
            bits |= Bit(b);
        return bits;
    }
}
