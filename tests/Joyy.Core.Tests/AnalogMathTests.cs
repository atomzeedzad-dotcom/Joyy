using Joyy.Core;

namespace Joyy.Core.Tests;

public class AnalogMathTests
{
    const double Dz = 0.08;

    [Fact]
    public void Center_stays_center()
    {
        Assert.Equal(((short)0, (short)0), AnalogMath.ApplyRadialDeadzone(0, 0, Dz, 1.0));
        Assert.Equal(((short)0, (short)0), AnalogMath.ApplyRadialDeadzone(0, 0, 0, 1.0));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(0, -1)]
    [InlineData(2000, -1500)]
    [InlineData(-1800, 1800)]
    public void Small_drift_inside_deadzone_is_zeroed(int x, int y)
    {
        Assert.Equal(((short)0, (short)0), AnalogMath.ApplyRadialDeadzone((short)x, (short)y, Dz, 1.0));
    }

    [Theory]
    [InlineData(32767, 0, 32767, 0)]
    [InlineData(-32768, 0, -32768, 0)]
    [InlineData(0, 32767, 0, 32767)]
    [InlineData(0, -32768, 0, -32768)]
    public void Full_deflection_reaches_full_range_despite_deadzone(int x, int y, int ex, int ey)
    {
        Assert.Equal(((short)ex, (short)ey), AnalogMath.ApplyRadialDeadzone((short)x, (short)y, Dz, 1.0));
    }

    [Fact]
    public void Zero_deadzone_is_identity_on_axes()
    {
        foreach (short v in new short[] { 1, -1, 100, -100, 16384, -16384, short.MaxValue, short.MinValue })
            Assert.Equal((v, (short)0), AnalogMath.ApplyRadialDeadzone(v, 0, 0, 1.0));
    }

    [Fact]
    public void Output_is_continuous_just_outside_deadzone()
    {
        short justOutside = (short)Math.Ceiling(Dz * 32767 + 50);
        var (x, _) = AnalogMath.ApplyRadialDeadzone(justOutside, 0, Dz, 1.0);
        Assert.InRange(x, (short)1, (short)200);
    }

    [Fact]
    public void Diagonal_keeps_direction_and_stays_on_unit_circle()
    {
        // Square-gate corner: magnitude ~1.41 must be clamped to 1, direction 45 degrees.
        var (x, y) = AnalogMath.ApplyRadialDeadzone(short.MaxValue, short.MaxValue, Dz, 1.0);
        Assert.Equal(x, y);
        double magnitude = Math.Sqrt((double)x * x + (double)y * y) / 32767.0;
        Assert.InRange(magnitude, 0.999, 1.0001);

        var (nx, ny) = AnalogMath.ApplyRadialDeadzone(short.MinValue, short.MinValue, Dz, 1.0);
        Assert.Equal(nx, ny);
        Assert.True(nx < -23000);
    }

    [Fact]
    public void Round_gate_diagonal_maps_to_full_diagonal()
    {
        var (x, y) = AnalogMath.ApplyRadialDeadzone(23170, -23170, Dz, 1.0);
        Assert.InRange(x, (short)23160, (short)23180);
        Assert.InRange(y, (short)-23180, (short)-23160);
    }

    [Fact]
    public void Outer_saturation_reaches_full_scale_early()
    {
        var (x, _) = AnalogMath.ApplyRadialDeadzone(29500, 0, 0.0, 0.9);
        Assert.Equal(short.MaxValue, x);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 255)]
    public void Trigger_endpoints(int raw, int expected)
    {
        Assert.Equal(expected, AnalogMath.ApplyTrigger((byte)raw, 0.02, 1.0));
        Assert.Equal(expected, AnalogMath.ApplyTrigger((byte)raw, 0.0, 1.0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Trigger_idle_drift_is_ignored(int raw)
    {
        Assert.Equal(0, AnalogMath.ApplyTrigger((byte)raw, 0.02, 1.0));
    }

    [Fact]
    public void Trigger_without_threshold_is_identity()
    {
        for (int raw = 0; raw <= 255; raw++)
            Assert.Equal((byte)raw, AnalogMath.ApplyTrigger((byte)raw, 0.0, 1.0));
    }

    [Fact]
    public void Trigger_is_monotonic_with_threshold_and_curve()
    {
        byte previous = 0;
        for (int raw = 0; raw <= 255; raw++)
        {
            byte value = AnalogMath.ApplyTrigger((byte)raw, 0.1, 2.0);
            Assert.True(value >= previous, $"not monotonic at {raw}");
            previous = value;
        }
        Assert.Equal(255, previous);
    }

    [Fact]
    public void Trigger_curve_shapes_midpoint()
    {
        Assert.InRange(AnalogMath.ApplyTrigger(128, 0, 2.0), (byte)62, (byte)66);  // 0.502^2 * 255
        Assert.InRange(AnalogMath.ApplyTrigger(128, 0, 0.5), (byte)179, (byte)182); // sqrt(0.502) * 255
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, -1)]
    [InlineData(-1, 1)]
    [InlineData(32767, -32767)]
    [InlineData(-32768, 32767)]
    public void Invert_never_overflows(int value, int expected)
    {
        Assert.Equal((short)expected, AnalogMath.Invert((short)value));
    }
}
