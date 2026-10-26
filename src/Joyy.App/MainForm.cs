using System.Diagnostics;
using Joyy.Core;
using Joyy.Platform;

namespace Joyy.App;

internal interface IAppActions
{
    void TogglePause();
    /// <summary>Saves the editor's profiles into the current config; other settings are left as they are now.</summary>
    string? SaveProfiles(Profile global, IReadOnlyList<Profile> games);
    void SetStartWithWindows(bool enabled);
    void SetStartMinimized(bool enabled);
    void SetHideDualSense(bool enabled);
    void OpenLogFolder();
    void OpenConfigFile();
    void Exit();
}

/// <summary>The "Hide DualSense" status line.</summary>
internal readonly record struct HidingView(string Text, bool Good, bool Warn, bool InstallLink);

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
    readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 14f), ForeColor = Theme.Text };
    readonly Label _detail = new() { AutoSize = true, ForeColor = Theme.Muted };
    readonly Button _pause = Theme.MakeButton("Pause");
    readonly Label _controller = Value(), _virtual = Value(), _profile = Value(), _others = Value();
    readonly LinkLabel _hiding = new()
    {
        AutoSize = true, MaximumSize = new Size(760, 0), LinkColor = Theme.Accent,
        ActiveLinkColor = Theme.Accent, VisitedLinkColor = Theme.Accent, ForeColor = Theme.Text,
    };
    readonly CheckBox _hideDualSense = Theme.MakeCheckBox("Hide the DualSense from games (recommended)");
    readonly CheckBox _startWithWindows = Theme.MakeCheckBox("Start Joyy with Windows");
    readonly CheckBox _startMinimized = Theme.MakeCheckBox("Start hidden in the tray");
    readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, Font = new Font("Cascadia Mono", 9f),
    };
    readonly List<string> _logLines = new();
    readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    readonly List<(Button Button, Control Page)> _pages = new();
    readonly FlowLayoutPanel _nav = new()
    {
        Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Theme.Card, WrapContents = false, Padding = new Padding(14, 0, 0, 0),
    };
    bool _allowClose;
    bool _loadingOptions;
    StatusView? _shownStatus;

    public MainForm(IAppActions actions)
    {
        _actions = actions;
        Text = "Joyy — DualSense to Xbox 360";
        Icon = Icons.App;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        ClientSize = new Size(1080, 760);
        MinimumSize = new Size(860, 640);
        StartPosition = FormStartPosition.CenterScreen;

        Profiles = new ProfileEditor(actions.SaveProfiles) { Dock = DockStyle.Fill };

        var header = BuildHeader();
        AddPage("Overview", BuildOverview());
        AddPage("Profiles", Profiles);
        AddPage("Log", BuildLog());

        Controls.Add(_content);
        Controls.Add(header);
        ShowPage(0);

        _pause.Click += (_, _) => _actions.TogglePause();
        _startWithWindows.CheckedChanged += (_, _) => { if (!_loadingOptions) _actions.SetStartWithWindows(_startWithWindows.Checked); };
        _startMinimized.CheckedChanged += (_, _) => { if (!_loadingOptions) _actions.SetStartMinimized(_startMinimized.Checked); };
        _hideDualSense.CheckedChanged += (_, _) => { if (!_loadingOptions) _actions.SetHideDualSense(_hideDualSense.Checked); };
        _hiding.LinkClicked += (_, _) => OpenUrl(DependencyChecker.HidHideUrl);
        Theme.DarkScrollbars(_log);

        foreach (var view in new[] { _dsView, _xbView })
            SetupPicture(view);
    }

    /// <summary>Loads the user's controller photo (if any) and adds the right-click menu to change it.</summary>
    void SetupPicture(PadView view)
    {
        try
        {
            if (PhotoSkin.Find(view.Kind) is { } path)
                view.Skin = PhotoSkin.Load(path);
        }
        catch (Exception)
        {
            view.Skin = null; // unreadable photo: fall back to the drawing
        }

        var menu = new ContextMenuStrip { Renderer = new Theme.MenuRenderer(), BackColor = Theme.Card, ForeColor = Theme.Text };
        menu.Items.Add("Use a photo of the controller…", null, (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = view.Kind == PadView.PadKind.DualSense ? "Choose a DualSense photo" : "Choose an Xbox controller photo",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                var old = view.Skin;
                view.Skin = PhotoSkin.Install(view.Kind, dialog.FileName);
                old?.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not use that picture: {ex.Message}", "Joyy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        });
        menu.Items.Add("Use the built-in drawing", null, (_, _) =>
        {
            var old = view.Skin;
            view.Skin = null;
            old?.Dispose();
            PhotoSkin.Remove(view.Kind);
        });
        view.ContextMenuStrip = menu;
    }

    public ProfileEditor Profiles { get; }

    /// <summary>Raised the first time the window is closed to the tray.</summary>
    public event Action? HiddenToTray;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

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

    public void SetEnvironment(HidingView hiding, IReadOnlyList<string> conflicts)
    {
        if (_hiding.Text != hiding.Text)
        {
            _hiding.Text = hiding.Text;
            int at = hiding.InstallLink ? hiding.Text.IndexOf("Install HidHide", StringComparison.Ordinal) : -1;
            _hiding.LinkArea = at >= 0 ? new LinkArea(at, "Install HidHide".Length) : new LinkArea(0, 0);
        }
        _hiding.ForeColor = hiding.Warn ? Theme.Warn : hiding.Good ? Theme.Good : Theme.Text;
        _hideDualSense.Enabled = !hiding.InstallLink;

        _others.Text = conflicts.Count == 0
            ? "None detected"
            : string.Join(", ", conflicts) + "  — these can also grab the DualSense and create a second controller.";
        _others.ForeColor = conflicts.Count == 0 ? Theme.Text : Theme.Warn;
    }

    public void SetStartupOptions(bool withWindows, bool minimized, bool hideDualSense)
    {
        _loadingOptions = true;
        _startWithWindows.Checked = withWindows;
        _startMinimized.Checked = minimized;
        _hideDualSense.Checked = hideDualSense;
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

    // ---- layout ----

    void AddPage(string title, Control page)
    {
        var button = new NavButton(title);
        int index = _pages.Count;
        button.Click += (_, _) => ShowPage(index);
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _content.Controls.Add(page);
        _nav.Controls.Add(button);
        _pages.Add((button, page));
    }

    void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Page.Visible = i == index;
            ((NavButton)_pages[i].Button).Selected = i == index;
        }
    }

    Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 118, BackColor = Theme.Card };

        var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Location = new Point(50, 14), WrapContents = false, BackColor = Theme.Card };
        text.Controls.Add(_status);
        text.Controls.Add(_detail);
        _dot.Location = new Point(22, 24);
        header.Controls.Add(_dot);
        header.Controls.Add(text);
        header.Controls.Add(_pause);
        header.Resize += (_, _) => _pause.Location = new Point(header.ClientSize.Width - _pause.Width - 20, 22);

        header.Controls.Add(_nav);

        var line = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border };
        header.Controls.Add(line);
        return header;
    }

    Control BuildOverview()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(14), BackColor = Theme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // controllers take what the status card leaves
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(Card("Your DualSense", "What you press  ·  right-click to change picture", _dsView, new Padding(0, 0, 7, 7)), 0, 0);
        root.Controls.Add(Card("Xbox controller", "What the game sees (Xbox 360)", _xbView, new Padding(7, 0, 0, 7)), 1, 0);

        var info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Card, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
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

        var options = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0), BackColor = Theme.Card };
        options.Controls.Add(_hideDualSense);
        options.Controls.Add(_startWithWindows);
        options.Controls.Add(_startMinimized);
        info.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        info.Controls.Add(options, 1, row);
        info.RowCount = row + 1;

        var card = Card("Status", null, info, new Padding(0, 7, 0, 0));
        card.Dock = DockStyle.Top;
        card.AutoSize = true;
        card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        info.Dock = DockStyle.Top;
        root.Controls.Add(card, 0, 1);
        root.SetColumnSpan(card, 2);
        return root;
    }

    Control BuildLog()
    {
        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Theme.Background };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 10, 0, 0), BackColor = Theme.Background };
        var openLogs = Theme.MakeButton("Open log folder");
        var openConfig = Theme.MakeButton("Open config.json");
        openLogs.Click += (_, _) => _actions.OpenLogFolder();
        openConfig.Click += (_, _) => _actions.OpenConfigFile();
        buttons.Controls.Add(openLogs);
        buttons.Controls.Add(openConfig);
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(12) };
        card.Controls.Add(_log);
        root.Controls.Add(card);
        root.Controls.Add(buttons);
        return root;
    }

    static Panel Card(string title, string? subtitle, Control content, Padding margin)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(16, 12, 16, 12), Margin = margin };
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, WrapContents = false, BackColor = Theme.Card };
        head.Controls.Add(Theme.Heading(title));
        if (subtitle is not null)
        {
            var sub = Theme.Caption(subtitle);
            sub.Padding = new Padding(4, 4, 0, 0);
            head.Controls.Add(sub);
        }
        content.Dock = DockStyle.Fill;
        content.BackColor = Theme.Card;
        card.Controls.Add(content);
        card.Controls.Add(head);
        return card;
    }

    static Label Value() => new() { AutoSize = true, ForeColor = Theme.Text, MaximumSize = new Size(760, 0) };

    static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    /// <summary>
    /// Header navigation item, fully owner-drawn: text plus an accent underline when selected.
    /// Size comes from the text measured at the current DPI, so it never clips.
    /// </summary>
    sealed class NavButton : Button
    {
        bool _selected;
        bool _hover;

        public NavButton(string text)
        {
            Text = text;
            AccessibleName = text;
            Font = new Font("Segoe UI Semibold", 10f);
            AutoSize = true;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, 2, 0);
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                Invalidate();
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size(text.Width + LogicalToDeviceUnits(28), text.Height + LogicalToDeviceUnits(18));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(_hover && !_selected ? Theme.Raised : Theme.Card))
                g.FillRectangle(bg, ClientRectangle);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, _selected ? Theme.Text : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (_selected)
            {
                int pad = LogicalToDeviceUnits(10), thick = LogicalToDeviceUnits(3);
                using var accent = new SolidBrush(Theme.Accent);
                g.FillRectangle(accent, pad, Height - thick, Width - pad * 2, thick);
            }
        }
    }

    /// <summary>The colored status circle in the header, with a soft halo.</summary>
    sealed class Dot : Control
    {
        Color _color = Theme.Idle;

        public Dot()
        {
            Size = new Size(20, 20);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
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
            e.Graphics.Clear(BackColor);
            using var halo = new SolidBrush(Color.FromArgb(60, _color));
            using var brush = new SolidBrush(_color);
            e.Graphics.FillEllipse(halo, 0, 0, Width - 1, Height - 1);
            e.Graphics.FillEllipse(brush, 4, 4, Width - 9, Height - 9);
        }
    }
}
