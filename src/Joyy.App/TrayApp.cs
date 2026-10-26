using System.Diagnostics;
using Joyy.Core;
using Joyy.Platform;
using Microsoft.Win32;

namespace Joyy.App;

/// <summary>
/// Owns everything for the life of the app: config, the input thread, the tray icon and the
/// window. Closing the window only hides it; Exit (tray menu) stops the bridge and quits.
/// </summary>
internal sealed class TrayApp : ApplicationContext, IAppActions
{
    readonly FileLog _log = new();
    readonly ConfigStore _store;
    readonly BridgeHost _host;
    readonly MainForm _form;
    readonly NotifyIcon _tray;
    readonly SynchronizationContext _ui;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    readonly RegisteredWaitHandle _showWait;
    readonly ToolStripMenuItem _menuStatus = new() { Enabled = false };
    readonly ToolStripMenuItem _menuProfile = new() { Enabled = false };
    readonly ToolStripMenuItem _menuPause = new("Pause");

    AppConfig _config;
    string? _lastForeground;
    StatusView? _trayStatus;
    bool _viGEmInstalled;
    bool _hidHideInstalled;
    IReadOnlyList<string> _conflicts = Array.Empty<string>();
    long _ticks;
    bool _toldAboutTray;
    bool _shutDown;

    public TrayApp(bool startHidden, EventWaitHandle showSignal)
    {
        _log.Write("INFO", $"Joyy starting ({Environment.ProcessPath})");

        _store = new ConfigStore();
        _config = _store.Load(out string? configError);

        _form = new MainForm(this);
        _ = _form.Handle; // creates the window handle and installs the WinForms SynchronizationContext
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _form.HiddenToTray += OnHiddenToTray;
        _form.Profiles.LoadConfig(_config);
        _form.SetStartupOptions(StartupRegistration.IsEnabled, _config.StartMinimized);

        _host = new BridgeHost(ProfileSelector.Select(_config, null));
        _host.Event += e => _ui.Post(_ => Report(e.Kind, e.Message), null);

        _store.Reloaded += c => _ui.Post(_ => OnConfigReloaded(c), null);
        _store.LoadFailed += error => _ui.Post(_ => Report(HostEventKind.Warning, $"config.json not reloaded: {error}"), null);

        _tray = new NotifyIcon { Icon = Icons.Waiting, Text = "Joyy", Visible = true, ContextMenuStrip = BuildMenu() };
        _tray.DoubleClick += (_, _) => ShowWindow();

        RefreshEnvironment();
        if (configError is not null)
            Report(HostEventKind.Warning, $"Could not read config, using defaults (file left untouched): {configError}");
        if (!_viGEmInstalled)
            Report(HostEventKind.Error, $"ViGEmBus driver not found. Install it from {DependencyChecker.ViGEmBusUrl}");

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        _host.Start();

        _showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => _ui.Post(_ => ShowWindow(), null), null, Timeout.Infinite, executeOnlyOnce: false);

        Application.ApplicationExit += (_, _) => Shutdown();
        SystemEvents.SessionEnding += (_, _) => Shutdown();

        if (!startHidden && !_config.StartMinimized)
            ShowWindow();
    }

    // ---- IAppActions ----

    public void TogglePause()
    {
        if (_host.IsRunning)
        {
            _host.Stop();
            Report(HostEventKind.Info, "Paused by user");
        }
        else
        {
            _host.Start();
        }
        bool paused = !_host.IsRunning;
        _menuPause.Text = paused ? "Resume" : "Pause";
        _form.SetPaused(paused);
    }

    public string? SaveConfig(AppConfig config)
    {
        try
        {
            _store.Save(config);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        _config = config;
        ApplyProfile(force: true);
        Report(HostEventKind.Info, "Settings saved");
        return null;
    }

    public void SetStartWithWindows(bool enabled)
    {
        try
        {
            StartupRegistration.Set(enabled, Environment.ProcessPath ?? Application.ExecutablePath);
            Report(HostEventKind.Info, enabled ? "Joyy will start with Windows" : "Joyy will no longer start with Windows");
        }
        catch (Exception ex)
        {
            Report(HostEventKind.Error, $"Could not change startup setting: {ex.Message}");
        }
    }

    public void SetStartMinimized(bool enabled)
    {
        var updated = _config with { StartMinimized = enabled };
        if (SaveConfig(updated) is { } error)
            Report(HostEventKind.Error, $"Could not save setting: {error}");
    }

    public void OpenLogFolder() => Launch("explorer.exe", $"\"{_log.Directory}\"");

    public void OpenConfigFile() => Launch("notepad.exe", $"\"{_store.FilePath}\"");

    public void Exit()
    {
        Shutdown();
        ExitThread();
    }

    // ---- internals ----

    void OnTick()
    {
        _ticks++;
        var snapshot = _host.Snapshot;
        var status = StatusView.From(snapshot, _host.IsRunning, _viGEmInstalled);

        if (_form.Visible)
            _form.UpdateLive(snapshot, status, _host.ActiveProfile);

        if (_trayStatus != status)
        {
            _trayStatus = status;
            _tray.Icon = status.Icon;
            string tip = $"Joyy: {status.Title}";
            _tray.Text = tip.Length > 63 ? tip[..63] : tip;
            _menuStatus.Text = status.Title;
        }

        if (_ticks % 20 == 0) // ~1 s: follow the foreground game
            ApplyProfile(force: false);
        if (_ticks % 100 == 0) // ~5 s: drivers and other tools
            RefreshEnvironment();
    }

    void ApplyProfile(bool force)
    {
        string? foreground = ForegroundProcess.GetName();
        if (foreground is not null)
            _lastForeground = foreground;

        var profile = ProfileSelector.Select(_config, _lastForeground);
        if (!force && ReferenceEquals(profile, _host.ActiveProfile))
            return;

        bool changed = !ReferenceEquals(profile, _host.ActiveProfile) &&
                       profile.ProcessName != _host.ActiveProfile.ProcessName;
        _host.SetProfile(profile);
        _menuProfile.Text = $"Profile: {(profile.ProcessName is null ? "All games" : profile.Name)}";
        if (changed)
            Report(HostEventKind.Info, $"Profile switched to {(profile.ProcessName is null ? "All games" : profile.Name)}");
    }

    void OnConfigReloaded(AppConfig config)
    {
        _config = config;
        _form.Profiles.ExternalReload(config);
        _form.SetStartupOptions(StartupRegistration.IsEnabled, config.StartMinimized);
        ApplyProfile(force: true);
        Report(HostEventKind.Info, "config.json changed on disk and was reloaded");
    }

    void RefreshEnvironment()
    {
        _viGEmInstalled = DependencyChecker.IsViGEmBusInstalled;
        _hidHideInstalled = DependencyChecker.IsHidHideInstalled;
        var conflicts = ConflictDetector.RunningConflicts();
        if (!conflicts.SequenceEqual(_conflicts))
        {
            foreach (var c in conflicts.Except(_conflicts))
                Report(HostEventKind.Warning, $"{c} is running and may also use the DualSense (possible double controller)");
            _conflicts = conflicts;
        }
        _form.SetEnvironment(_hidHideInstalled, _conflicts);
    }

    void Report(HostEventKind kind, string message)
    {
        _log.Write(kind.ToString().ToUpperInvariant(), message);
        if (!_shutDown)
            _form.AppendLog(DateTime.Now, kind, message);
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_menuStatus);
        menu.Items.Add(_menuProfile);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Joyy", null, (_, _) => ShowWindow());
        _menuPause.Click += (_, _) => TogglePause();
        menu.Items.Add(_menuPause);
        menu.Items.Add(new ToolStripMenuItem("Restore controller visibility (needs HidHide support)") { Enabled = false });
        menu.Items.Add("Open logs", null, (_, _) => OpenLogFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        _menuProfile.Text = "Profile: All games";
        return menu;
    }

    void ShowWindow()
    {
        if (_shutDown)
            return;
        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized)
            _form.WindowState = FormWindowState.Normal;
        _form.Activate();
        _form.BringToFront();
    }

    void OnHiddenToTray()
    {
        if (_toldAboutTray)
            return;
        _toldAboutTray = true;
        _tray.ShowBalloonTip(4000, "Joyy is still running",
            "Your controller keeps working in the background. Right-click the tray icon to exit.", ToolTipIcon.Info);
    }

    void Shutdown()
    {
        if (_shutDown)
            return;
        _shutDown = true;
        _timer.Stop();
        _host.Dispose(); // unplugs the virtual controller
        _showWait.Unregister(null);
        _store.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _form.CloseForReal();
        _log.Write("INFO", "Joyy stopped");
    }

    void Launch(string exe, string args)
    {
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Report(HostEventKind.Error, $"Could not open {exe}: {ex.Message}"); }
    }
}
