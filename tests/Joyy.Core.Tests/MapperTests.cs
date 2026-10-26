using Joyy.Core;

namespace Joyy.Core.Tests;

public class MapperTests
{
    static readonly Mapper DefaultMapper = new(new Profile());

    static PadState Press(params PhysicalButton[] buttons) =>
        PadState.Neutral with { Buttons = PhysicalButtons.Of(buttons) };

    // Every row of spec section 5.2.
    [Theory]
    [InlineData(PhysicalButton.Cross, X360Buttons.A)]
    [InlineData(PhysicalButton.Circle, X360Buttons.B)]
    [InlineData(PhysicalButton.Square, X360Buttons.X)]
    [InlineData(PhysicalButton.Triangle, X360Buttons.Y)]
    [InlineData(PhysicalButton.L1, X360Buttons.LeftShoulder)]
    [InlineData(PhysicalButton.R1, X360Buttons.RightShoulder)]
    [InlineData(PhysicalButton.L3, X360Buttons.LeftThumb)]
    [InlineData(PhysicalButton.R3, X360Buttons.RightThumb)]
    [InlineData(PhysicalButton.Options, X360Buttons.Start)]
    [InlineData(PhysicalButton.Create, X360Buttons.Back)]
    [InlineData(PhysicalButton.PS, X360Buttons.Guide)]
    [InlineData(PhysicalButton.Touchpad, X360Buttons.Back)]
    [InlineData(PhysicalButton.Mute, X360Buttons.None)]
    [InlineData(PhysicalButton.DpadUp, X360Buttons.DpadUp)]
    [InlineData(PhysicalButton.DpadDown, X360Buttons.DpadDown)]
    [InlineData(PhysicalButton.DpadLeft, X360Buttons.DpadLeft)]
    [InlineData(PhysicalButton.DpadRight, X360Buttons.DpadRight)]
    public void Default_mapping_is_position_based(PhysicalButton physical, X360Buttons expected)
    {
        var report = DefaultMapper.Map(Press(physical));
        Assert.Equal(expected, report.Buttons);
    }

    [Fact]
    public void Default_table_covers_every_physical_button()
    {
        foreach (var b in Enum.GetValues<PhysicalButton>())
            Assert.Contains(b, ButtonMap.Default);
    }

    [Fact]
    public void Analog_triggers_map_to_LT_and_RT_not_shoulders()
    {
        var report = DefaultMapper.Map(PadState.Neutral with { L2 = 255, R2 = 128 });
        Assert.Equal(255, report.LeftTrigger);
        Assert.InRange(report.RightTrigger, 125, 128);
        Assert.Equal(X360Buttons.None, report.Buttons);
    }

    [Fact]
    public void Neutral_input_gives_neutral_report()
    {
        Assert.Equal(X360Report.Neutral, DefaultMapper.Map(PadState.Neutral));
    }

    [Fact]
    public void Multiple_buttons_combine()
    {
        var report = DefaultMapper.Map(Press(PhysicalButton.Cross, PhysicalButton.R1, PhysicalButton.DpadLeft));
        Assert.Equal(X360Buttons.A | X360Buttons.RightShoulder | X360Buttons.DpadLeft, report.Buttons);
    }

    [Fact]
    public void Unknown_high_bits_are_ignored()
    {
        var report = DefaultMapper.Map(PadState.Neutral with { Buttons = 0x8000_0000u | PhysicalButtons.Bit(PhysicalButton.Cross) });
        Assert.Equal(X360Buttons.A, report.Buttons);
    }

    [Theory]
    [InlineData(PhysicalButton.Cross, X360Buttons.B)]
    [InlineData(PhysicalButton.Circle, X360Buttons.A)]
    [InlineData(PhysicalButton.Square, X360Buttons.Y)]
    [InlineData(PhysicalButton.Triangle, X360Buttons.X)]
    [InlineData(PhysicalButton.R1, X360Buttons.RightShoulder)]
    public void Nintendo_layout_swaps_only_face_buttons(PhysicalButton physical, X360Buttons expected)
    {
        var mapper = new Mapper(new Profile { NintendoLayout = true });
        Assert.Equal(expected, mapper.Map(Press(physical)).Buttons);
    }

    [Fact]
    public void Overrides_replace_defaults_and_are_not_swapped()
    {
        var mapper = new Mapper(new Profile
        {
            NintendoLayout = true,
            ButtonOverrides = new Dictionary<PhysicalButton, X360Buttons>
            {
                [PhysicalButton.Mute] = X360Buttons.Guide,
                [PhysicalButton.Touchpad] = X360Buttons.None,
                [PhysicalButton.Cross] = X360Buttons.A,
            },
        });

        Assert.Equal(X360Buttons.Guide, mapper.Map(Press(PhysicalButton.Mute)).Buttons);
        Assert.Equal(X360Buttons.None, mapper.Map(Press(PhysicalButton.Touchpad)).Buttons);
        Assert.Equal(X360Buttons.A, mapper.Map(Press(PhysicalButton.Cross)).Buttons);
        Assert.Equal(X360Buttons.A, mapper.Map(Press(PhysicalButton.Circle)).Buttons);
    }

    [Fact]
    public void Sticks_pass_through_with_XInput_Y_up_convention()
    {
        var mapper = new Mapper(new Profile { StickDeadzone = 0 });
        var report = mapper.Map(PadState.Neutral with { LeftY = short.MaxValue, RightX = short.MinValue });
        Assert.Equal(short.MaxValue, report.ThumbLY);
        Assert.Equal(short.MinValue, report.ThumbRX);
        Assert.Equal(0, report.ThumbLX);
        Assert.Equal(0, report.ThumbRY);
    }

    [Fact]
    public void Invert_flags_negate_each_axis()
    {
        var mapper = new Mapper(new Profile
        {
            StickDeadzone = 0,
            InvertLeftX = true,
            InvertLeftY = true,
            InvertRightX = true,
            InvertRightY = true,
        });
        var report = mapper.Map(new PadState(0, 20000, -20000, short.MinValue, 0, 0, 0));
        Assert.Equal(-20000, report.ThumbLX);
        Assert.Equal(20000, report.ThumbLY);
        Assert.Equal(short.MaxValue, report.ThumbRX);
        Assert.Equal(0, report.ThumbRY);
    }

    [Fact]
    public void Constructor_rejects_invalid_profile()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Mapper(new Profile { StickDeadzone = 0.95 }));
    }
}
