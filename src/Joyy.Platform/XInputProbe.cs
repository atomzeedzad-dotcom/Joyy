using System.Runtime.InteropServices;
using Joyy.Core;

namespace Joyy.Platform;

/// <summary>
/// Reads controllers the way an XInput game does. Used for self-tests and diagnostics,
/// never in the input path.
/// </summary>
public static partial class XInputProbe
{
    const uint ErrorSuccess = 0;

    // XINPUT_GAMEPAD_EX: the Ex call writes a trailing reserved DWORD that plain XINPUT_GAMEPAD lacks.
    [StructLayout(LayoutKind.Sequential)]
    struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct State
    {
        public uint PacketNumber;
        public Gamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Vibration
    {
        public ushort Left, Right;
    }

    // Ordinal 100 is XInputGetStateEx: same as XInputGetState but also reports the Guide button.
    [LibraryImport("xinput1_4.dll", EntryPoint = "#100")]
    private static partial uint XInputGetStateEx(uint userIndex, out State state);

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static partial uint XInputSetState(uint userIndex, ref Vibration vibration);

    /// <summary>Returns the report XInput sees in the given slot, or null if no controller is there.</summary>
    public static X360Report? Read(int userIndex)
    {
        if (XInputGetStateEx((uint)userIndex, out var s) != ErrorSuccess)
            return null;
        var g = s.Gamepad;
        return new X360Report((X360Buttons)g.Buttons, g.LeftTrigger, g.RightTrigger,
            g.ThumbLX, g.ThumbLY, g.ThumbRX, g.ThumbRY);
    }

    /// <summary>Slots 0..3 that currently have a controller.</summary>
    public static IEnumerable<int> ConnectedSlots() =>
        Enumerable.Range(0, 4).Where(i => Read(i) is not null);

    /// <summary>Sets rumble the way a game would. Motor speeds are 0..65535.</summary>
    public static bool SetRumble(int userIndex, ushort left, ushort right)
    {
        var v = new Vibration { Left = left, Right = right };
        return XInputSetState((uint)userIndex, ref v) == ErrorSuccess;
    }
}
