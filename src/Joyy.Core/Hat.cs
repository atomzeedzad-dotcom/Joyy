namespace Joyy.Core;

/// <summary>Eight-way hat switch plus centered (9 states).</summary>
public enum Hat : byte
{
    Centered,
    Up,
    UpRight,
    Right,
    DownRight,
    Down,
    DownLeft,
    Left,
    UpLeft,
}

public static class HatConverter
{
    public static readonly uint DpadMask = PhysicalButtons.Of(
        PhysicalButton.DpadUp, PhysicalButton.DpadDown, PhysicalButton.DpadLeft, PhysicalButton.DpadRight);

    static readonly uint Up = PhysicalButtons.Bit(PhysicalButton.DpadUp);
    static readonly uint Down = PhysicalButtons.Bit(PhysicalButton.DpadDown);
    static readonly uint Left = PhysicalButtons.Bit(PhysicalButton.DpadLeft);
    static readonly uint Right = PhysicalButtons.Bit(PhysicalButton.DpadRight);

    /// <summary>Hat direction to D-pad bits. Unknown values are treated as centered.</summary>
    public static uint ToButtons(Hat hat) => hat switch
    {
        Hat.Up => Up,
        Hat.UpRight => Up | Right,
        Hat.Right => Right,
        Hat.DownRight => Down | Right,
        Hat.Down => Down,
        Hat.DownLeft => Down | Left,
        Hat.Left => Left,
        Hat.UpLeft => Up | Left,
        _ => 0,
    };

    /// <summary>Replaces the D-pad bits of <paramref name="buttons"/> with the given hat.</summary>
    public static uint Apply(uint buttons, Hat hat) => (buttons & ~DpadMask) | ToButtons(hat);
}
