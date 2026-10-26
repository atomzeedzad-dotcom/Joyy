using Joyy.Core;

namespace Joyy.Core.Tests;

public class HatConverterTests
{
    const PhysicalButton U = PhysicalButton.DpadUp;
    const PhysicalButton D = PhysicalButton.DpadDown;
    const PhysicalButton L = PhysicalButton.DpadLeft;
    const PhysicalButton R = PhysicalButton.DpadRight;

    public static TheoryData<Hat, PhysicalButton[]> AllStates => new()
    {
        { Hat.Centered, Array.Empty<PhysicalButton>() },
        { Hat.Up, new[] { U } },
        { Hat.UpRight, new[] { U, R } },
        { Hat.Right, new[] { R } },
        { Hat.DownRight, new[] { D, R } },
        { Hat.Down, new[] { D } },
        { Hat.DownLeft, new[] { D, L } },
        { Hat.Left, new[] { L } },
        { Hat.UpLeft, new[] { U, L } },
    };

    [Theory]
    [MemberData(nameof(AllStates))]
    public void Every_hat_state_maps_to_dpad_bits(Hat hat, PhysicalButton[] expected)
    {
        Assert.Equal(PhysicalButtons.Of(expected), HatConverter.ToButtons(hat));
    }

    [Theory]
    [MemberData(nameof(AllStates))]
    public void Hat_bits_reach_X360_dpad_through_mapper(Hat hat, PhysicalButton[] expected)
    {
        var mapper = new Mapper(new Profile());
        var report = mapper.Map(PadState.Neutral with { Buttons = HatConverter.ToButtons(hat) });

        X360Buttons want = X360Buttons.None;
        foreach (var b in expected)
            want |= ButtonMap.Default[b];
        Assert.Equal(want, report.Buttons);
    }

    [Fact]
    public void Unknown_hat_value_is_centered()
    {
        Assert.Equal(0u, HatConverter.ToButtons((Hat)200));
    }

    [Fact]
    public void Apply_replaces_previous_dpad_and_keeps_other_buttons()
    {
        uint before = PhysicalButtons.Of(PhysicalButton.Cross, U, L);
        uint after = HatConverter.Apply(before, Hat.DownRight);
        Assert.Equal(PhysicalButtons.Of(PhysicalButton.Cross, D, R), after);
        Assert.Equal(PhysicalButtons.Bit(PhysicalButton.Cross), HatConverter.Apply(after, Hat.Centered));
    }
}
