namespace Joyy.Core;

/// <summary>
/// Stick and trigger shaping. All work is done on normalized doubles; conversion back
/// to 16-bit / 8-bit happens last, so deadzones act on the true deflection.
/// </summary>
public static class AnalogMath
{
    /// <summary>
    /// Radial deadzone with rescaling: output starts at 0 just outside the deadzone and
    /// reaches full scale at <paramref name="saturation"/>. Direction is preserved and
    /// the output magnitude never exceeds 1 (so diagonals stay on the unit circle).
    /// </summary>
    public static (short X, short Y) ApplyRadialDeadzone(short rawX, short rawY, double deadzone, double saturation)
    {
        double x = Normalize(rawX);
        double y = Normalize(rawY);
        double magnitude = Math.Sqrt(x * x + y * y);
        if (magnitude <= deadzone || magnitude == 0)
            return (0, 0);

        double scaled = Math.Min(1.0, (magnitude - deadzone) / (saturation - deadzone));
        double k = scaled / magnitude;
        return (ToShort(x * k), ToShort(y * k));
    }

    /// <summary>Applies the rest threshold, rescales to full range, then the curve exponent.</summary>
    public static byte ApplyTrigger(byte raw, double threshold, double curve)
    {
        double t = raw / 255.0;
        if (t <= threshold)
            return 0;

        double n = (t - threshold) / (1.0 - threshold);
        if (curve != 1.0)
            n = Math.Pow(n, curve);
        return (byte)Math.Round(Math.Clamp(n, 0.0, 1.0) * 255.0);
    }

    /// <summary>Converts a 0..32767 trigger axis (SDL's range) to 0..255; negative input counts as released.</summary>
    public static byte Trigger15To8(short value) =>
        value <= 0 ? (byte)0 : (byte)Math.Round(value * 255.0 / 32767.0);

    /// <summary>
    /// Converts an Xbox 360 motor strength (0..255, as ViGEm reports it) to SDL's 0..65535
    /// rumble range, scaled by the profile's rumble factor.
    /// </summary>
    public static ushort RumbleToSdl(byte motor, double scale) =>
        (ushort)Math.Clamp(Math.Round(motor * 257.0 * scale), 0, ushort.MaxValue);

    /// <summary>Negates an axis without overflowing at short.MinValue.</summary>
    public static short Invert(short value) => value == short.MinValue ? short.MaxValue : (short)-value;

    static double Normalize(short v) => v < 0 ? v / 32768.0 : v / 32767.0;

    static short ToShort(double v)
    {
        v = Math.Clamp(v, -1.0, 1.0);
        return (short)Math.Round(v < 0 ? v * 32768.0 : v * 32767.0);
    }
}
