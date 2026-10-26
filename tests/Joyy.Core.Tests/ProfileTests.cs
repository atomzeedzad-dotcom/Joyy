using Joyy.Core;

namespace Joyy.Core.Tests;

public class ProfileTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        new Profile().Validate();
    }

    public static TheoryData<Profile> InvalidProfiles => new()
    {
        new Profile { StickDeadzone = -0.01 },
        new Profile { StickDeadzone = 0.91 },
        new Profile { StickDeadzone = double.NaN },
        new Profile { StickDeadzone = 0.5, StickOuterSaturation = 0.5 },
        new Profile { StickOuterSaturation = 1.01 },
        new Profile { TriggerThreshold = 0.95 },
        new Profile { TriggerCurve = 0.0 },
        new Profile { TriggerCurve = 11 },
        new Profile { RumbleScale = -1 },
        new Profile { RumbleScale = 2.5 },
    };

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public void Out_of_range_values_are_rejected(Profile profile)
    {
        Assert.Throws<ArgumentOutOfRangeException>(profile.Validate);
    }
}
