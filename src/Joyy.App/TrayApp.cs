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
    readonly RegisteredWaitHandle _quitWait;
    Process? _guardian;
    readonly ToolStripMenuItem _menuStatus = new() { Enabled = false };
    readonly ToolStripMenuItem _menuProfile = new() { Enabled = false };
    readonly ToolStripMenuItem _menuPause = new("Pause");
    readonly ToolStripMenuItem _menuHide = new("Hide DualSense from games") { CheckOnClick = false };
    readonly DeviceHider? _hider;
    readonly object _hidingGate = new();
    Task _hidingWork = Task.CompletedTask;
    volatile string? _hidingError;
    volatile bool _hidingActive;
    volatile bool _replugNeeded;
    string? _lastDevicePath;

    AppConfig _config;
    string? _lastForeground;
    StatusView? _trayStatus;
    bool _viGEmInstalled;
    IReadOnlyList<string> _conflicts = Array.Empty<string>();
    long _ticks;
    bool _toldAboutTray;
    bool _shutDown;

    public TrayApp(bool startHidden, EventWaitHandle showSignal, EventWaitHandle quitSignal)
    {
        _log.Write("INFO", $"Joyy starting ({Environment.ProcessPath})");

        _store = new ConfigStore();
        _config = _store.Load(out string? configError);

        _form = new MainForm(this);
        _ = _form.Handle; // creates the window handle and installs the WinForms SynchronizationContext
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _form.HiddenToTray += OnHiddenToTray;
        _form.Session.Load(_config);

        if (HidHideCli.Locate() is { } hidHideCli)
            _hider = new DeviceHider(new HidHideCli(hidHideCli, m => _log.Write("HIDHIDE", m)),
                Environment.ProcessPath ?? Application.ExecutablePath);

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
        QueueHiding(recover: true);

        _showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => _ui.Post(_ => ShowWindow(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        // "Joyy.exe --quit" (used by the installer when updating) asks us to exit cleanly.
        _quitWait = ThreadPool.RegisterWaitForSingleObject(quitSignal,
            (_, _) => _ui.Post(_ => Exit(), null), null, Timeout.Infinite, executeOnlyOnce: true);

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
        QueueHiding(); // paused: give the DualSense back to games; resumed: hide it again
    }

    public void SetVirtualDevice(bool on)
    {
        if (on != _host.IsRunning)
            TogglePause();
    }

    public void TestRumble(double strength) => _host.PulseRumble(strength);

    public string? SaveProfiles(Profile global, IReadOnlyList<Profile> games) =>
        SaveConfig(_config with { Global = global, Games = games });

    public void SetHideDualSense(bool enabled)
    {
        if (SaveConfig(_config with { HideDualSense = enabled }) is { } error)
        {
            Report(HostEventKind.Error, $"Could not save setting: {error}");
            return;
        }
        RefreshEnvironment();
        QueueHiding();
    }

    string? SaveConfig(AppConfig config)
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
            RefreshEnvironment();
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
        RefreshEnvironment();
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
            _form.UpdateLive(new LiveView(snapshot, status, _host.ActiveProfile, _host.IsRunning));

        if (_trayStatus != status)
        {
            _trayStatus = status;
            _tray.Icon = status.Icon;
            string tip = $"Joyy: {status.Title}";
            _tray.Text = tip.Length > 63 ? tip[..63] : tip;
            _menuStatus.Text = status.Title;
        }

        // A DualSense appeared (or came back on another connection): make sure it is hidden.
        string? devicePath = snapshot.Device?.DevicePath;
        if (devicePath != _lastDevicePath)
        {
            _lastDevicePath = devicePath;
            if (devicePath is not null)
                QueueHiding();
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
        bool hidingChanged = config.HideDualSense != _config.HideDualSense;
        _config = config;
        _form.Session.ExternalReload(config);
        RefreshEnvironment();
        ApplyProfile(force: true);
        if (hidingChanged)
            QueueHiding();
        Report(HostEventKind.Info, "config.json changed on disk and was reloaded");
    }

    /// <summary>
    /// Brings HidHide in line with what is wanted right now: DualSense hidden while the bridge runs
    /// and the setting is on, visible otherwise. HidHideCLI calls are slow-ish, so this runs on a
    /// worker, one request after another.
    /// </summary>
    void QueueHiding(bool recover = false)
    {
        if (_hider is null || _shutDown)
            return;
        bool want = _config.HideDualSense && _host.IsRunning;
        lock (_hidingGate)
            _hidingWork = _hidingWork.ContinueWith(_ => UpdateHiding(want, recover), TaskScheduler.Default);
    }

    void UpdateHiding(bool want, bool recover)
    {
        void Post(HostEventKind kind, string message) => _ui.Post(_ => Report(kind, message), null);
        try
        {
            if (recover && _hider!.RecoverIfNeeded())
                Post(HostEventKind.Warning, "Joyy did not exit cleanly last time; the DualSense was made visible again first");

            if (want)
            {
                bool wasActive = _hidingActive, wasReplug = _replugNeeded;
                int count = _hider!.Apply();
                if (count > 0)
                    EnsureGuardian();
                _hidingActive = count > 0;
                _replugNeeded = _hider.HiddenByJoyy.Any(p => DeviceStack.HasHidHideFilter(p) == false);
                if (_hidingActive && (!wasActive || wasReplug != _replugNeeded) && !_replugNeeded)
                    Post(HostEventKind.Info, $"DualSense hidden from games ({count} device{(count == 1 ? "" : "s")}); only Joyy can see it");
                if (_replugNeeded && !wasReplug)
                    Post(HostEventKind.Warning, "HidHide is not attached to your DualSense yet: unplug it and plug it back in once (or restart Windows)");
            }
            else
            {
                bool had = _hider!.HasPendingRestore;
                _hider.Restore();
                _hidingActive = false;
                _replugNeeded = false;
                if (had)
                    Post(HostEventKind.Info, "DualSense is visible to games again");
            }
            _hidingError = null;
        }
        catch (Exception ex)
        {
            _hidingError = ex.Message;
            Post(HostEventKind.Error, $"Device hiding failed: {ex.Message}");
        }
        _ui.Post(_ => RefreshEnvironment(), null);
    }

    /// <summary>
    /// Starts the small "--guardian" helper that gives the DualSense back if this process dies
    /// without cleaning up, so the controller never stays hidden with Joyy closed.
    /// </summary>
    void EnsureGuardian()
    {
        if (_guardian is { HasExited: false })
            return;
        try
        {
            _guardian?.Dispose();
            _guardian = Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? Application.ExecutablePath,
                $"--guardian {Environment.ProcessId}")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            });
            _log.Write("INFO", "Guardian started: the DualSense is given back automatically if Joyy dies");
        }
        catch (Exception ex)
        {
            _log.Write("WARNING", $"Could not start the guardian: {ex.Message}");
        }
    }

    HidingView DescribeHiding()
    {
        if (_hider is null)
            return new("Off. Games can also see the DualSense itself, which may cause double input. Install HidHide to fix this.",
                Good: false, Warn: true, InstallLink: true);
        if (!_config.HideDualSense)
            return new("Off (turned off below). Games can also see the DualSense itself, which may cause double input.", false, true, false);
        if (!_host.IsRunning)
            return new("Paused, so the DualSense is visible to games again.", false, false, false);
        if (_hidingError is { } error)
            return new($"Error: {error}", false, true, false);
        if (_replugNeeded)
            return new("Almost done: unplug the DualSense and plug it back in once (or restart Windows) so HidHide can hide it.", false, true, false);
        if (_hidingActive)
            return new("On. Games see only the Xbox controller. Restart any game that was already open before Joyy.", true, false, false);
        return new("Waiting for a DualSense to hide.", false, false, false);
    }

    void RefreshEnvironment()
    {
        _viGEmInstalled = DependencyChecker.IsViGEmBusInstalled;
        var conflicts = ConflictDetector.RunningConflicts();
        if (!conflicts.SequenceEqual(_conflicts))
        {
            foreach (var c in conflicts.Except(_conflicts))
                Report(HostEventKind.Warning, $"{c} is running and may also use the DualSense (possible double controller)");
            _conflicts = conflicts;
        }
        _form.SetEnvironment(new EnvView(DescribeHiding(), _conflicts, _viGEmInstalled, _hider is not null,
            _config.HideDualSense, _hidingActive && _host.IsRunning, _replugNeeded,
            StartupRegistration.IsEnabled, _config.StartMinimized));
        _menuHide.Enabled = _hider is not null;
        _menuHide.Checked = _hider is not null && _config.HideDualSense;
    }

    void Report(HostEventKind kind, string message)
    {
        _log.Write(kind.ToString().ToUpperInvariant(), message);
        if (!_shutDown)
            _form.AppendLog(DateTime.Now, kind, message);
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new Theme.MenuRenderer(), BackColor = Theme.Card, ForeColor = Theme.Text };
        menu.Items.Add(_menuStatus);
        menu.Items.Add(_menuProfile);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Joyy", null, (_, _) => ShowWindow());
        _menuPause.Click += (_, _) => TogglePause();
        menu.Items.Add(_menuPause);
        _menuHide.Click += (_, _) => SetHideDualSense(!_config.HideDualSense);
        menu.Items.Add(_menuHide);
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

        // Give the DualSense back to games. If this fails (or Joyy is killed before getting here),
        // the journal makes the next start, or "Joyy.exe --restore", finish the job.
        if (_hider is not null)
        {
            try
            {
                _hidingWork.Wait(TimeSpan.FromSeconds(10));
                _hider.Restore();
                _log.Write("INFO", "DualSense visibility restored on exit");
            }
            catch (Exception ex)
            {
                _log.Write("ERROR", $"Could not restore DualSense visibility on exit: {ex.Message}");
            }
        }
        _showWait.Unregister(null);
        _quitWait.Unregister(null);
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
