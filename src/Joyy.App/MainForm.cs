using System.Diagnostics;
using Joyy.Core;
using Joyy.Platform;

namespace Joyy.App;

internal interface IAppActions
{
    void TogglePause();
    string? SaveConfig(AppConfig config);
    void SetStartWithWindows(bool enabled);
    void SetStartMinimized(bool enabled);
    void OpenLogFolder();
    void OpenConfigFile();
    void Exit();
}

/// <summary>What the header and tray say for a given bridge state.</summary>
internal readonly record struct StatusView(Color Color, Icon Icon, string Title, string Detail)
{
    public static StatusView From(BridgeSnapshot s, bool hostRunning, bool viGEmInstalled)
    {
        if (!s.Running)
            return hostRunning
                ? new(Theme.Idle, Icons.Waiting, "Starting…", "")
                : new(Theme.Idle, Icons.Waiting, "Paused", "The virtual Xbox controller is unplugged. Press Resume to continue.");

        return s.Status switch
        {
            BridgeStatus.Running => new(Theme.Good, Icons.Ready, "Ready to play",
                $"DualSense{(s.Device is { } d ? $" ({Connection(d.Connection)})" : "")} → Xbox 360 controller" +
                (s.VirtualSlot is int i ? $" in slot {i + 1}" : "")),
            BridgeStatus.WaitingForController => new(Theme.Idle, Icons.Waiting, "Waiting for your DualSense",
                "Connect it with a USB cable or Bluetooth."),
            _ => new(Theme.Bad, Icons.Error, "Virtual controller unavailable",
                viGEmInstalled ? s.Detail ?? "ViGEmBus reported an error." : "The ViGEmBus driver is not installed."),
        };
    }

    public static string Connection(ConnectionKind kind) => kind switch
    {
        ConnectionKind.Usb => "USB",
        ConnectionKind.Bluetooth => "Bluetooth",
        _ => "connected",
    };
}

internal sealed class MainForm : Form
{
    const int MaxLogLines = 500;

    readonly IAppActions _actions;
    readonly PadView _dsView = new(PadView.PadKind.DualSense) { Dock = DockStyle.Fill };
    readonly PadView _xbView = new(PadView.PadKind.Xbox) { Dock = DockStyle.Fill };
    readonly Dot _dot = new();
    readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = Theme.Text };
    readonly Label _detail = new() { AutoSize = true, ForeColor = Theme.Muted };
    readonly Button _pause = new() { Text = "Pause", AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
    readonly Label _controller = Value(), _virtual = Value(), _profile = Value(), _others = Value();
    readonly LinkLabel _hiding = new() { AutoSize = true, MaximumSize = new Size(760, 0), LinkColor = Theme.Accent };
    readonly CheckBox _startWithWindows = new() { Text = "Start Joyy with Windows", AutoSize = true };
    readonly CheckBox _startMinimized = new() { Text = "Start hidden in the tray", AutoSize = true };
    readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Card, Font = new Font("Consolas", 9f) };
    readonly List<string> _logLines = new();
    bool _allowClose;
    bool _loadingOptions;
    StatusView? _shownStatus;

    public MainForm(IAppActions actions)
    {
        _actions = actions;
        Text = "Joyy — DualSense to Xbox 360";
        Icon = Icons.App;
        BackColor = Theme.Background;
        ClientSize = new Size(980, 640);
        MinimumSize = new Size(760, 540);
        StartPosition = FormStartPosition.CenterScreen;

        Profiles = new ProfileEditor(actions.SaveConfig) { Dock = DockStyle.Fill };

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        tabs.TabPages.Add(Page("Overview", BuildOverview()));
        tabs.TabPages.Add(Page("Profiles", Profiles));
        tabs.TabPages.Add(Page("Log", BuildLog()));

        Controls.Add(tabs);
        Controls.Add(BuildHeader());

        _pause.Click += (_, _) => _actions.TogglePause();
        _startWithWindows.CheckedChanged += (_, _) => { if (!_loadingOptions) _actions.SetStartWithWindows(_startWithWindows.Checked); };
        _startMinimized.CheckedChanged += (_, _) => { if (!_loadingOptions) _actions.SetStartMinimized(_startMinimized.Checked); };
        _hiding.LinkClicked += (_, _) => OpenUrl(DependencyChecker.HidHideUrl);
    }

    public ProfileEditor Profiles { get; }

    /// <summary>Raised the first time the window is closed to the tray.</summary>
    public event Action? HiddenToTray;

    public void UpdateLive(BridgeSnapshot s, StatusView status, Profile active)
    {
        bool live = s.Running && s.Device is not null;
        _dsView.SetState(s.Input, s.Output, live);
        _xbView.SetState(s.Input, s.Output, live && s.Status == BridgeStatus.Running);

        if (_shownStatus != status)
        {
            _shownStatus = status;
            _dot.Color = status.Color;
            _status.Text = status.Title;
            _detail.Text = status.Detail;
        }

        _controller.Text = s.Device is { } d
            ? $"{d.Name}  ·  {StatusView.Connection(d.Connection)}  ·  serial {d.Serial ?? "unknown"}"
            : "Not connected";
        _virtual.Text = s.Running && s.Status == BridgeStatus.Running
            ? $"Xbox 360 controller plugged in{(s.VirtualSlot is int i ? $" (XInput slot {i + 1})" : "")}"
            : "Not plugged in";
        _profile.Text = active.ProcessName is null
            ? "All games (global)"
            : $"{active.Name}  ({ProfileSelector.NormalizeProcessName(active.ProcessName)}.exe)";
    }

    public void SetPaused(bool paused) => _pause.Text = paused ? "Resume" : "Pause";

    public void SetEnvironment(bool hidHideInstalled, IReadOnlyList<string> conflicts)
    {
        string hiding = hidHideInstalled
            ? "HidHide is installed. Automatic hiding of the DualSense arrives in the next Joyy update."
            : "Off. Games can also see the DualSense itself, which may cause double input. Install HidHide to fix this.";
        if (_hiding.Text != hiding)
        {
            _hiding.Text = hiding;
            int at = hiding.IndexOf("Install HidHide", StringComparison.Ordinal);
            _hiding.LinkArea = at >= 0 ? new LinkArea(at, "Install HidHide".Length) : new LinkArea(0, 0);
        }

        _others.Text = conflicts.Count == 0
            ? "None detected"
            : string.Join(", ", conflicts) + "  — these can also grab the DualSense and create a second controller.";
        _others.ForeColor = conflicts.Count == 0 ? Theme.Text : Theme.Warn;
    }

    public void SetStartupOptions(bool withWindows, bool minimized)
    {
        _loadingOptions = true;
        _startWithWindows.Checked = withWindows;
        _startMinimized.Checked = minimized;
        _loadingOptions = false;
    }

    public void AppendLog(DateTime time, HostEventKind kind, string message)
    {
        _logLines.Add($"{time:HH:mm:ss}  {kind,-7}  {message}");
        if (_logLines.Count > MaxLogLines)
            _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
        _log.Lines = _logLines.ToArray();
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
            return;
        }
        base.OnFormClosing(e);
    }

    Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Card, Padding = new Padding(18, 12, 18, 10) };
        var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Location = new Point(48, 10), WrapContents = false };
        text.Controls.Add(_status);
        text.Controls.Add(_detail);
        _dot.Location = new Point(20, 20);
        _pause.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(_dot);
        header.Controls.Add(text);
        header.Controls.Add(_pause);
        header.Resize += (_, _) => _pause.Location = new Point(header.ClientSize.Width - _pause.Width - 18, 22);
        return header;
    }

    Control BuildOverview()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12), BackColor = Theme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        root.Controls.Add(Card("Your DualSense", "What you press", _dsView, new Padding(0, 0, 6, 6)), 0, 0);
        root.Controls.Add(Card("Xbox 360 controller", "What the game sees", _xbView, new Padding(6, 0, 0, 6)), 1, 0);

        var info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        void AddRow(string label, Control value)
        {
            info.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            info.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 5, 0, 5) }, 0, row);
            value.Margin = new Padding(0, 5, 0, 5);
            info.Controls.Add(value, 1, row);
            row++;
        }
        AddRow("Controller", _controller);
        AddRow("Virtual controller", _virtual);
        AddRow("Active profile", _profile);
        AddRow("Hide DualSense", _hiding);
        AddRow("Other tools", _others);

        var options = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        options.Controls.Add(_startWithWindows);
        options.Controls.Add(_startMinimized);
        info.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        info.Controls.Add(options, 1, row);
        info.RowCount = row + 1;

        var card = Card("Status", null, info, new Padding(0, 6, 0, 0));
        root.Controls.Add(card, 0, 1);
        root.SetColumnSpan(card, 2);
        return root;
    }

    Control BuildLog()
    {
        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Theme.Background };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        var openLogs = new Button { Text = "Open log folder", AutoSize = true };
        var openConfig = new Button { Text = "Open config.json", AutoSize = true };
        openLogs.Click += (_, _) => _actions.OpenLogFolder();
        openConfig.Click += (_, _) => _actions.OpenConfigFile();
        buttons.Controls.Add(openLogs);
        buttons.Controls.Add(openConfig);
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(10) };
        card.Controls.Add(_log);
        root.Controls.Add(card);
        root.Controls.Add(buttons);
        return root;
    }

    static Control Card(string title, string? subtitle, Control content, Padding margin)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(14, 10, 14, 10), Margin = margin };
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, WrapContents = false };
        head.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = Theme.Text });
        if (subtitle is not null)
            head.Controls.Add(new Label { Text = subtitle, AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(4, 3, 0, 0) });
        content.Dock = DockStyle.Fill;
        card.Controls.Add(content);
        card.Controls.Add(head);
        return card;
    }

    static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { BackColor = Theme.Background, UseVisualStyleBackColor = false };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    static Label Value() => new() { AutoSize = true, ForeColor = Theme.Text, MaximumSize = new Size(760, 0) };

    static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    /// <summary>The colored status circle in the header.</summary>
    sealed class Dot : Control
    {
        Color _color = Theme.Idle;

        public Dot()
        {
            Size = new Size(18, 18);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Card;
        }

        public Color Color
        {
            get => _color;
            set { _color = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_color);
            e.Graphics.FillEllipse(brush, 1, 1, Width - 3, Height - 3);
        }
    }
}
