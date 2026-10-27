using System.Drawing.Drawing2D;
using Joyy.Core;
using Joyy.Platform;

namespace Joyy.App;

internal interface IAppActions
{
    void TogglePause();
    /// <summary>Virtual Xbox controller on (bridge running) or off (paused).</summary>
    void SetVirtualDevice(bool on);
    /// <summary>Saves the editor's profiles into the current config; other settings are left as they are now.</summary>
    string? SaveProfiles(Profile global, IReadOnlyList<Profile> games);
    void SetStartWithWindows(bool enabled);
    void SetStartMinimized(bool enabled);
    void SetHideDualSense(bool enabled);
    void TestRumble(double strength);
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

/// <summary>
/// Black shell: a slim top bar (logo, status, active profile, power) above one page at a time.
/// Home shows the controller with feature tiles; every other page opens in the same area with a
/// back button and its title.
/// </summary>
internal sealed class MainForm : Form
{
    readonly ShellContext _ctx;
    readonly Dictionary<string, Page> _pages = new();
    readonly ClearPanel _host = new() { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 0) };
    readonly ClearPanel _header = new() { Dock = DockStyle.Top, Height = 74, Padding = new Padding(24, 6, 24, 8) };
    readonly Label _title = new() { AutoSize = true, UseMnemonic = false, ForeColor = Theme.Text, BackColor = Color.Transparent, Font = new Font("Segoe UI Semibold", 16f) };
    readonly Label _subtitle = new() { AutoSize = true, UseMnemonic = false, ForeColor = Theme.Muted, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9.25f) };
    readonly StatusPill _status = new();
    readonly ProfileChip _profile = new();
    readonly RoundIconButton _power = new(Theme.Glyph.Power) { Size = new Size(36, 36), BackColor = Theme.Background };
    readonly LogPage _log;
    string _current = "";
    bool _allowClose;

    public MainForm(IAppActions actions)
    {
        Text = "Joyy";
        Icon = Icons.App;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        DoubleBuffered = true;
        ClientSize = new Size(1120, 860);
        MinimumSize = new Size(980, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;

        Session = new ProfileSession(actions.SaveProfiles);
        _ctx = new ShellContext(actions, Session);
        LoadSkins();
        _log = new LogPage(_ctx);

        foreach (var (key, page) in new (string, Page)[]
        {
            ("home", new HomePage(_ctx)),
            ("controller", new ControllerPage(_ctx)),
            ("profiles", new ProfilesPage(_ctx)),
            ("mapping", new MappingPage(_ctx)),
            ("sticks", new SticksPage(_ctx)),
            ("hide", new HidePage(_ctx)),
            ("log", _log),
            ("settings", new SettingsPage(_ctx)),
        })
        {
            page.Visible = false;
            _host.Controls.Add(page);
            _pages[key] = page;
        }

        _power.Click += (_, _) => actions.TogglePause();
        _profile.Click += (_, _) => ShowPage("profiles");
        SetPaused(false);

        BuildHeader();
        Controls.Add(_host);
        Controls.Add(_header);
        Controls.Add(BuildTopBar());

        _ctx.NavigateRequested += ShowPage;
        ShowPage("home");
    }

    public ProfileSession Session { get; }

    /// <summary>Raised the first time the window is closed to the tray.</summary>
    public event Action? HiddenToTray;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintBackground(e.Graphics, ClientRectangle);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && _current != "home")
        {
            ShowPage("home");
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    void LoadSkins()
    {
        foreach (var kind in new[] { PadView.PadKind.DualSense, PadView.PadKind.Xbox })
        {
            try
            {
                var skin = PhotoSkin.Find(kind) is { } path ? PhotoSkin.Load(path) : null;
                if (kind == PadView.PadKind.DualSense)
                    _ctx.DualSenseSkin = skin;
                else
                    _ctx.XboxSkin = skin;
            }
            catch (Exception)
            {
                // Unreadable photo: fall back to the built-in drawing.
            }
        }
    }

    Control BuildTopBar()
    {
        var bar = new ClearPanel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(24, 0, 24, 0) };
        var logo = new Logo { Size = new Size(160, 40) };
        bar.Controls.Add(logo);
        bar.Controls.Add(_status);
        bar.Controls.Add(_profile);
        bar.Controls.Add(_power);
        void Arrange()
        {
            int mid = bar.Height / 2;
            int right = bar.Width - bar.Padding.Right;
            logo.Location = new Point(bar.Padding.Left, mid - logo.Height / 2);
            _power.Location = new Point(right - _power.Width, mid - _power.Height / 2);
            _profile.Location = new Point(_power.Left - 10 - _profile.Width, mid - _profile.Height / 2);
            _status.Location = new Point(_profile.Left - 10 - _status.Width, mid - _status.Height / 2);
        }
        bar.Layout += (_, _) => Arrange();
        _status.SizeChanged += (_, _) => Arrange();
        _profile.SizeChanged += (_, _) => Arrange();
        return bar;
    }

    void BuildHeader()
    {
        var back = new RoundIconButton(Theme.Glyph.Back) { Size = new Size(38, 38), Color = Theme.Text, BackColor = Theme.Background, AccessibleName = "Back to Home" };
        back.Click += (_, _) => ShowPage("home");
        var titles = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, BackColor = Color.Transparent, WrapContents = false, Margin = Padding.Empty };
        _title.Margin = new Padding(0);
        _subtitle.Margin = new Padding(2, 0, 0, 0);
        titles.Controls.Add(_title);
        titles.Controls.Add(_subtitle);
        _header.Controls.Add(back);
        _header.Controls.Add(titles);
        _header.Layout += (_, _) =>
        {
            back.Location = new Point(_header.Padding.Left, 12);
            titles.Location = new Point(back.Right + 14, 4);
        };
    }

    public void ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out var shown))
            return;
        _current = key;
        SuspendLayout();
        _header.Visible = key != "home";
        foreach (var (k, page) in _pages)
            page.Visible = k == key;
        _title.Text = shown.Title;
        _subtitle.Text = shown.Subtitle;
        ResumeLayout(true);
        shown.OnEnvironment(_ctx.Env);
        shown.OnLive(_ctx.Live);
        shown.OnActivated();
    }

    public void UpdateLive(LiveView live)
    {
        _ctx.Live = live;
        var s = live.Snapshot;
        string text = live.Status.Title;
        if (s.Running && s.Status == BridgeStatus.Running && s.VirtualSlot is int slot)
            text += $"  ·  Xbox 360 slot {slot + 1}";
        _status.Set(text, live.Status.Color);
        _profile.SetText(ProfileSession.NameOf(live.Active));
        if (Visible && _pages.TryGetValue(_current, out var p))
            p.OnLive(live);
    }

    public void SetEnvironment(EnvView env)
    {
        _ctx.Env = env;
        foreach (var page in _pages.Values)
            page.OnEnvironment(env);
    }

    public void SetPaused(bool paused)
    {
        _power.Color = paused ? Theme.Good : Theme.Bad;
        _power.AccessibleName = paused ? "Resume" : "Pause";
        _power.Invalidate();
    }

    public void AppendLog(DateTime time, HostEventKind kind, string message) => _log.Append(time, kind, message);

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

    /// <summary>App mark (white tile, black controller) + name.</summary>
    sealed class Logo : Control
    {
        public Logo()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = LogicalToDeviceUnits(34);
            var mark = new RectangleF(0, (Height - d) / 2, d, d);
            using (var path = Theme.RoundedRect(mark, LogicalToDeviceUnits(9)))
            using (var fill = new SolidBrush(Theme.Text))
                g.FillPath(fill, path);
            using (var icon = Theme.IconFont(13f))
                TextRenderer.DrawText(g, Theme.Glyph.Controller, icon, Rectangle.Round(mark), Theme.Background, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using var font = new Font("Segoe UI Semibold", 15f);
            TextRenderer.DrawText(g, "Joyy", font, new Rectangle((int)mark.Right + LogicalToDeviceUnits(10), 0, Width, Height), Theme.Text, TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>Top-bar status: a colored dot and "Ready to play · Xbox 360 slot 1" in a pill.</summary>
internal sealed class StatusPill : Control
{
    string _text = "";
    Color _dot = Theme.Idle;

    public StatusPill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
        Font = new Font("Segoe UI", 9f);
        Height = 34;
        Set("Starting…", Theme.Idle);
    }

    public void Set(string text, Color dot)
    {
        if (text == _text && dot == _dot)
            return;
        (_text, _dot) = (text, dot);
        AccessibleName = text;
        Width = TextRenderer.MeasureText(text, Font).Width + LogicalToDeviceUnits(46);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundedRect(r, r.Height / 2))
        {
            using var fill = new SolidBrush(Theme.Card);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        float d = LogicalToDeviceUnits(8);
        var dot = new RectangleF(LogicalToDeviceUnits(16), (Height - d) / 2, d, d);
        using (var halo = new SolidBrush(Color.FromArgb(60, _dot)))
            g.FillEllipse(halo, RectangleF.Inflate(dot, 3, 3));
        using (var fill = new SolidBrush(_dot))
            g.FillEllipse(fill, dot);
        int x = (int)dot.Right + LogicalToDeviceUnits(10);
        TextRenderer.DrawText(g, _text, Font, new Rectangle(x, 0, Width - x, Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Top-bar chip naming the profile in use; opens Profiles.</summary>
internal sealed class ProfileChip : OwnerButton
{
    public ProfileChip()
    {
        BackColor = Theme.Background;
        Font = new Font("Segoe UI", 9f);
        Height = 34;
        SetText("All games");
    }

    public void SetText(string name)
    {
        if (name == Text)
            return;
        Text = name;
        AccessibleName = $"Profile: {name}";
        Width = TextRenderer.MeasureText(name, Font).Width + Px(58);
        Invalidate();
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRounded(g, r, r.Height / 2, Pressed ? Theme.Border : Hovered ? Theme.Hover : Theme.Card);
        using (var path = Theme.RoundedRect(r, r.Height / 2))
        using (var pen = new Pen(Theme.Border))
            g.DrawPath(pen, path);
        DrawGlyph(g, Theme.Glyph.Profiles, new Rectangle(Px(12), 0, Px(18), Height), Theme.Muted, 9.5f);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Px(34), 0, Width - Px(56), Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        DrawGlyph(g, "", new Rectangle(Width - Px(26), 0, Px(14), Height), Theme.Muted, 7f); // chevron down
    }
}

/// <summary>Round icon button (power, back).</summary>
internal sealed class RoundIconButton : OwnerButton
{
    readonly string _glyph;

    public RoundIconButton(string glyph)
    {
        _glyph = glyph;
        AccessibleName = "Pause";
    }

    public Color Color { get; set; } = Theme.Bad;

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using (var fill = new SolidBrush(Hovered ? Color.FromArgb(60, Color) : Color.FromArgb(28, Color)))
            g.FillEllipse(fill, r);
        using (var pen = new Pen(Color.FromArgb(160, Color), 1.4f))
            g.DrawEllipse(pen, r);
        DrawGlyph(g, _glyph, ClientRectangle, Color, 10f);
    }
}
