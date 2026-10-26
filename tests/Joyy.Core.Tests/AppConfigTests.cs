using System.Text.Json;
using Joyy.Core;

namespace Joyy.Core.Tests;

public class AppConfigTests
{
    static AppConfig Sample() => new()
    {
        Global = new Profile { Name = "Global", StickDeadzone = 0.1, NintendoLayout = true },
        Games = new[]
        {
            new Profile
            {
                Name = "Old Racer",
                ProcessName = "racer.exe",
                TriggerCurve = 2.0,
                RumbleScale = 0.5,
                InvertRightY = true,
                ButtonOverrides = new Dictionary<PhysicalButton, X360Buttons>
                {
                    [PhysicalButton.Mute] = X360Buttons.Guide,
                    [PhysicalButton.Touchpad] = X360Buttons.None,
                },
            },
        },
        StartMinimized = true,
    };

    [Fact]
    public void Round_trips_through_json()
    {
        var original = Sample();
        var copy = ConfigSerializer.Deserialize(ConfigSerializer.Serialize(original));

        Assert.Equal(0.1, copy.Global.StickDeadzone);
        Assert.True(copy.Global.NintendoLayout);
        Assert.True(copy.StartMinimized);
        var game = Assert.Single(copy.Games);
        Assert.Equal("racer.exe", game.ProcessName);
        Assert.Equal(2.0, game.TriggerCurve);
        Assert.True(game.InvertRightY);
        Assert.Equal(X360Buttons.Guide, game.ButtonOverrides[PhysicalButton.Mute]);
        Assert.Equal(X360Buttons.None, game.ButtonOverrides[PhysicalButton.Touchpad]);
    }

    [Fact]
    public void Enums_are_written_as_names_for_hand_editing()
    {
        string json = ConfigSerializer.Serialize(Sample());
        Assert.Contains("\"Mute\": \"Guide\"", json);
    }

    [Fact]
    public void Missing_fields_use_defaults()
    {
        var config = ConfigSerializer.Deserialize("{ }");
        Assert.Equal(0.08, config.Global.StickDeadzone);
        Assert.Empty(config.Games);
        Assert.False(config.StartMinimized);
    }

    [Fact]
    public void Accepts_comments_trailing_commas_and_any_case()
    {
        var config = ConfigSerializer.Deserialize("""
            {
              // hand-edited
              "global": { "stickDeadzone": 0.2, },
            }
            """);
        Assert.Equal(0.2, config.Global.StickDeadzone);
    }

    [Fact]
    public void Malformed_json_throws()
    {
        Assert.ThrowsAny<JsonException>(() => ConfigSerializer.Deserialize("{ not json"));
    }

    [Fact]
    public void Out_of_range_values_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConfigSerializer.Deserialize("""{ "Global": { "StickDeadzone": 5 } }"""));
    }

    [Fact]
    public void Game_profile_without_process_is_rejected()
    {
        Assert.Throws<FormatException>(() =>
            ConfigSerializer.Deserialize("""{ "Games": [ { "Name": "x" } ] }"""));
    }

    [Fact]
    public void Duplicate_process_names_are_rejected()
    {
        Assert.Throws<FormatException>(() => ConfigSerializer.Deserialize(
            """{ "Games": [ { "ProcessName": "a.exe" }, { "ProcessName": "A" } ] }"""));
    }

    [Theory]
    [InlineData("racer", "Old Racer")]
    [InlineData("RACER.EXE", "Old Racer")]
    [InlineData(" racer.exe ", "Old Racer")]
    [InlineData("explorer", "Global")]
    [InlineData("", "Global")]
    [InlineData(null, "Global")]
    public void Selects_game_profile_by_process_name(string? process, string expected)
    {
        Assert.Equal(expected, ProfileSelector.Select(Sample(), process).Name);
    }
}
