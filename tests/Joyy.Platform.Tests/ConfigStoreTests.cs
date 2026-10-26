using Joyy.Core;
using Joyy.Platform;

namespace Joyy.Platform.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "joyy-tests-" + Guid.NewGuid().ToString("N"));
    string ConfigPath => Path.Combine(_dir, "config.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Missing_file_is_created_with_defaults()
    {
        using var store = new ConfigStore(ConfigPath, watch: false);
        var config = store.Load(out var error);

        Assert.Null(error);
        Assert.True(File.Exists(ConfigPath));
        Assert.Equal(0.08, config.Global.StickDeadzone);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        using var store = new ConfigStore(ConfigPath, watch: false);
        store.Save(new AppConfig { Global = new Profile { Name = "Global", RumbleScale = 0.25 } });

        var config = store.Load(out var error);
        Assert.Null(error);
        Assert.Equal(0.25, config.Global.RumbleScale);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Broken_file_reports_error_and_is_left_untouched()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, "{ broken");
        using var store = new ConfigStore(ConfigPath, watch: false);

        var config = store.Load(out var error);
        Assert.NotNull(error);
        Assert.Equal(0.08, config.Global.StickDeadzone);
        Assert.Equal("{ broken", File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void Invalid_config_is_not_saved()
    {
        using var store = new ConfigStore(ConfigPath, watch: false);
        store.Save(new AppConfig());
        string before = File.ReadAllText(ConfigPath);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Save(new AppConfig { Global = new Profile { StickDeadzone = 3 } }));
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public async Task External_edit_is_hot_reloaded()
    {
        using var store = new ConfigStore(ConfigPath);
        store.Load(out _);
        var reloaded = new TaskCompletionSource<AppConfig>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Reloaded += c => reloaded.TrySetResult(c);

        File.WriteAllText(ConfigPath, """{ "Global": { "StickDeadzone": 0.3 } }""");

        var config = await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0.3, config.Global.StickDeadzone);
    }
}
