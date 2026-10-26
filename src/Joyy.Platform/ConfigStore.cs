using Joyy.Core;

namespace Joyy.Platform;

/// <summary>
/// Loads and saves config.json and watches it for hand edits. A broken file never replaces
/// the working config: <see cref="LoadFailed"/> is raised and the previous config stays active.
/// </summary>
public sealed class ConfigStore : IDisposable
{
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Joyy", "config.json");

    readonly FileSystemWatcher? _watcher;
    readonly System.Threading.Timer _debounce;
    string? _lastWritten;

    public ConfigStore(string? path = null, bool watch = true)
    {
        FilePath = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        _debounce = new System.Threading.Timer(_ => ReloadFromDisk());

        if (watch)
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(FilePath)!, Path.GetFileName(FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            FileSystemEventHandler onChange = (_, _) => _debounce.Change(300, Timeout.Infinite);
            _watcher.Changed += onChange;
            _watcher.Created += onChange;
            _watcher.Renamed += (_, _) => _debounce.Change(300, Timeout.Infinite);
            _watcher.EnableRaisingEvents = true;
        }
    }

    public string FilePath { get; }

    /// <summary>Raised on a thread-pool thread after a valid external edit.</summary>
    public event Action<AppConfig>? Reloaded;

    /// <summary>Raised on a thread-pool thread when the file on disk cannot be used.</summary>
    public event Action<string>? LoadFailed;

    /// <summary>Loads the config, creating a default file if there is none. Returns defaults (and an error) if the file is broken.</summary>
    public AppConfig Load(out string? error)
    {
        error = null;
        if (!File.Exists(FilePath))
        {
            var fresh = new AppConfig();
            Save(fresh);
            return fresh;
        }

        try
        {
            return ConfigSerializer.Deserialize(File.ReadAllText(FilePath));
        }
        catch (Exception ex)
        {
            error = $"{FilePath}: {ex.Message}";
            return new AppConfig();
        }
    }

    /// <summary>Validates, then writes atomically (temp file + replace).</summary>
    public void Save(AppConfig config)
    {
        config.Validate();
        string json = ConfigSerializer.Serialize(config);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, FilePath, overwrite: true);
        Volatile.Write(ref _lastWritten, json);
    }

    void ReloadFromDisk()
    {
        string json;
        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (IOException)
        {
            _debounce.Change(300, Timeout.Infinite); // editor still holds the file
            return;
        }
        catch (Exception ex)
        {
            LoadFailed?.Invoke(ex.Message);
            return;
        }

        if (json == Volatile.Read(ref _lastWritten))
            return; // our own save

        try
        {
            var config = ConfigSerializer.Deserialize(json);
            Volatile.Write(ref _lastWritten, json);
            Reloaded?.Invoke(config);
        }
        catch (Exception ex)
        {
            LoadFailed?.Invoke(ex.Message);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
    }
}
