using System.Text.Json;
using System.Text.Json.Serialization;

namespace Joyy.Core;

/// <summary>Everything stored in %APPDATA%\Joyy\config.json.</summary>
public sealed record AppConfig
{
    public int Version { get; init; } = 1;

    /// <summary>Used whenever no game profile matches the foreground process.</summary>
    public Profile Global { get; init; } = new() { Name = "Global" };

    /// <summary>Per-game profiles, matched by <see cref="Profile.ProcessName"/>.</summary>
    public IReadOnlyList<Profile> Games { get; init; } = Array.Empty<Profile>();

    /// <summary>Start hidden in the tray instead of showing the window.</summary>
    public bool StartMinimized { get; init; }

    /// <summary>Hide the physical DualSense from games (via HidHide) while Joyy runs.</summary>
    public bool HideDualSense { get; init; } = true;

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> or <see cref="FormatException"/> if any profile is invalid.</summary>
    public void Validate()
    {
        Global.Validate();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in Games)
        {
            game.Validate();
            string process = ProfileSelector.NormalizeProcessName(game.ProcessName);
            if (process.Length == 0)
                throw new FormatException($"Game profile '{game.Name}' has no process name.");
            if (!seen.Add(process))
                throw new FormatException($"More than one game profile uses '{process}'.");
        }
    }
}

public static class ConfigSerializer
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, Options);

    /// <summary>Parses and validates. Throws <see cref="JsonException"/>, <see cref="FormatException"/> or <see cref="ArgumentOutOfRangeException"/>.</summary>
    public static AppConfig Deserialize(string json)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(json, Options)
            ?? throw new FormatException("Config file is empty.");

        // Missing sections in hand-edited files fall back to defaults rather than null.
        config = config with
        {
            Global = config.Global ?? new Profile { Name = "Global" },
            Games = config.Games ?? Array.Empty<Profile>(),
        };
        config.Validate();
        return config;
    }
}

public static class ProfileSelector
{
    /// <summary>"Game.EXE " and "game" both become "game".</summary>
    public static string NormalizeProcessName(string? processName)
    {
        string name = (processName ?? "").Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }

    /// <summary>The game profile for <paramref name="processName"/>, or the global profile.</summary>
    public static Profile Select(AppConfig config, string? processName)
    {
        string wanted = NormalizeProcessName(processName);
        if (wanted.Length > 0)
        {
            foreach (var game in config.Games)
            {
                if (string.Equals(NormalizeProcessName(game.ProcessName), wanted, StringComparison.OrdinalIgnoreCase))
                    return game;
            }
        }
        return config.Global;
    }
}
