using Joyy.Core;

namespace Joyy.App;

/// <summary>
/// The profiles being edited, shared by the Profiles, Button Mapping and Sticks &amp; Triggers
/// pages. Edits stay in memory until <see cref="Save"/>; <see cref="Changed"/> fires after any
/// change so every page can refresh.
/// </summary>
internal sealed class ProfileSession
{
    readonly Func<Profile, IReadOnlyList<Profile>, string?> _save;
    AppConfig _saved = new();

    public ProfileSession(Func<Profile, IReadOnlyList<Profile>, string?> save) => _save = save;

    /// <summary>[0] is the global profile, the rest are per-game profiles.</summary>
    public List<Profile> Profiles { get; } = new() { new Profile { Name = "Global" } };

    public int Current { get; private set; }
    public bool Dirty { get; private set; }

    /// <summary>Set when config.json changed on disk while there were unsaved edits.</summary>
    public bool ExternalChangePending { get; private set; }

    public Profile Editing => Profiles[Current];

    public event Action? Changed;

    public static string NameOf(Profile p) => p.ProcessName is null ? "All games" : p.Name;

    public static string DetailOf(Profile p) => p.ProcessName is null
        ? "Used for every game without its own profile"
        : $"Used while {ProfileSelector.NormalizeProcessName(p.ProcessName)}.exe is in front";

    public void Load(AppConfig config)
    {
        _saved = config;
        Profiles.Clear();
        Profiles.Add(config.Global);
        Profiles.AddRange(config.Games);
        Current = Math.Min(Current, Profiles.Count - 1);
        Dirty = false;
        ExternalChangePending = false;
        Changed?.Invoke();
    }

    public void ExternalReload(AppConfig config)
    {
        if (Dirty)
        {
            _saved = config;
            ExternalChangePending = true;
            Changed?.Invoke();
            return;
        }
        Load(config);
    }

    public void Select(int index)
    {
        Current = Math.Clamp(index, 0, Profiles.Count - 1);
        Changed?.Invoke();
    }

    /// <summary>Replaces the profile being edited. Throws if a value is out of range.</summary>
    public void Update(Func<Profile, Profile> change)
    {
        var updated = change(Editing);
        updated.Validate();
        Profiles[Current] = updated;
        Dirty = true;
        Changed?.Invoke();
    }

    /// <summary>Adds a game profile (starting from the global settings). Returns an error or null.</summary>
    public string? AddGame(string displayName, string processName)
    {
        string process = ProfileSelector.NormalizeProcessName(processName);
        if (process.Length == 0)
            return "Enter the game's program name.";
        if (Profiles.Skip(1).Any(p => string.Equals(ProfileSelector.NormalizeProcessName(p.ProcessName), process, StringComparison.OrdinalIgnoreCase)))
            return $"{process}.exe already has a profile.";
        Profiles.Add(Profiles[0] with { Name = displayName.Length > 0 ? displayName : process, ProcessName = process + ".exe" });
        Current = Profiles.Count - 1;
        Dirty = true;
        Changed?.Invoke();
        return null;
    }

    public void Remove(int index)
    {
        if (index <= 0 || index >= Profiles.Count)
            return;
        Profiles.RemoveAt(index);
        Current = Math.Min(Current, Profiles.Count - 1);
        Dirty = true;
        Changed?.Invoke();
    }

    public void ResetCurrent()
    {
        var p = Editing;
        Profiles[Current] = new Profile { Name = p.Name, ProcessName = p.ProcessName };
        Dirty = true;
        Changed?.Invoke();
    }

    /// <summary>Validates and writes config.json. Returns an error message or null.</summary>
    public string? Save()
    {
        string? error = _save(Profiles[0], Profiles.Skip(1).ToList());
        if (error is not null)
            return error;
        _saved = _saved with { Global = Profiles[0], Games = Profiles.Skip(1).ToList() };
        Dirty = false;
        ExternalChangePending = false;
        Changed?.Invoke();
        return null;
    }

    public void Revert() => Load(_saved);
}
