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
/// DSX-style shell: gradient window, sidebar navigation, a controller chip at the top and one
/// page at a time below it.
/// </summary>
internal sealed class MainForm : Form
{
    readonly ShellContext _ctx;
    readonly Dictionary<string, (NavItem Nav, Page Page)> _pages = new();
    readonly ClearPanel _host = new() { Dock = DockStyle.Fill };
    readonly Label _title = new() { AutoSize = true, UseMnemonic = false, ForeColor = Theme.Text, BackColor = Color.Transparent, Font = new Font("Segoe UI Semibold", 17f) };
    readonly Label _subtitle = new() { AutoSize = true, ForeColor = Theme.Muted, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9.5f) };
    readonly ControllerChip _chip;
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
        ClientSize = new Size(1260, 820);
        MinimumSize = new Size(1080, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        Session = new ProfileSession(actions.SaveProfiles);
        _ctx = new ShellContext(actions, Session);
        LoadSkins();

        _chip = new ControllerChip(actions) { Location = new Point(0, 0) };
        _log = new LogPage(_ctx);

        var sidebar = BuildSidebar();
        var content = new ClearPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 16, 0, 0) };
        var top = new ClearPanel { Dock = DockStyle.Top, Height = 132 };
        top.Controls.Add(_chip);
        var titles = new FlowLayoutPanel { Location = new Point(0, 70), AutoSize = true, FlowDirection = FlowDirection.TopDown, BackColor = Color.Transparent, WrapContents = false };
        titles.Controls.Add(_title);
        titles.Controls.Add(_subtitle);
        top.Controls.Add(titles);
        content.Controls.Add(_host);
        content.Controls.Add(top);
        Controls.Add(content);
        Controls.Add(sidebar);

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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
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

    Control BuildSidebar()
    {
        var bar = new ClearPanel { Dock = DockStyle.Left, Width = 236, Padding = new Padding(10, 10, 10, 14) };
        var logo = new Logo { Dock = DockStyle.Top, Height = 64 };

        var pages = new (string Key, string Glyph, string Name, Page Page)[]
        {
            ("home", Theme.Glyph.Home, "Home", new HomePage(_ctx)),
            ("controller", Theme.Glyph.Controller, "My Controller", new ControllerPage(_ctx)),
            ("profiles", Theme.Glyph.Profiles, "Profiles", new ProfilesPage(_ctx)),
            ("mapping", Theme.Glyph.Mapping, "Button Mapping", new MappingPage(_ctx)),
            ("sticks", Theme.Glyph.Sticks, "Sticks & Triggers", new SticksPage(_ctx)),
            ("hide", Theme.Glyph.Hide, "Hide DualSense", new HidePage(_ctx)),
            ("log", Theme.Glyph.Log, "Log", _log),
        };
        var settings = ("settings", Theme.Glyph.Settings, "Settings", (Page)new SettingsPage(_ctx));

        var navs = new List<Control> { logo };
        foreach (var (key, glyph, name, page) in pages)
        {
            if (key is "profiles" or "hide")
                navs.Add(new Panel { Height = 10, BackColor = Color.Transparent });
            navs.Add(AddPage(key, glyph, name, page));
        }
        var settingsNav = AddPage(settings.Item1, settings.Item2, settings.Item3, settings.Item4);
        settingsNav.Dock = DockStyle.Bottom;
        bar.Controls.Add(settingsNav);
        for (int i = navs.Count - 1; i >= 0; i--)
        {
            navs[i].Dock = DockStyle.Top;
            bar.Controls.Add(navs[i]);
        }
        return bar;
    }

    NavItem AddPage(string key, string glyph, string name, Page page)
    {
        var nav = new NavItem(glyph, name);
        nav.Click += (_, _) => ShowPage(key);
        page.Visible = false;
        _host.Controls.Add(page);
        _pages[key] = (nav, page);
        return nav;
    }

    public void ShowPage(string key)
    {
        if (!_pages.ContainsKey(key))
            return;
        _current = key;
        foreach (var (k, (nav, page)) in _pages)
        {
            nav.Selected = k == key;
            page.Visible = k == key;
        }
        var shown = _pages[key].Page;
        _title.Text = shown.Title;
        _subtitle.Text = shown.Subtitle;
        shown.OnEnvironment(_ctx.Env);
        shown.OnLive(_ctx.Live);
        shown.OnActivated();
    }

    public void UpdateLive(LiveView live)
    {
        _ctx.Live = live;
        _chip.Update(live, _ctx.Env);
        if (Visible && _pages.TryGetValue(_current, out var p))
            p.Page.OnLive(live);
    }

    public void SetEnvironment(EnvView env)
    {
        _ctx.Env = env;
        foreach (var (_, (_, page)) in _pages)
            page.OnEnvironment(env);
    }

    public void SetPaused(bool paused) => _chip.SetPaused(paused);

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

    /// <summary>App mark + name at the top of the sidebar.</summary>
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
            var mark = new RectangleF(LogicalToDeviceUnits(10), LogicalToDeviceUnits(14), LogicalToDeviceUnits(34), LogicalToDeviceUnits(34));
            using (var path = Theme.RoundedRect(mark, LogicalToDeviceUnits(9)))
            using (var fill = new LinearGradientBrush(mark, Theme.Accent, Color.FromArgb(0x6C, 0x5C, 0xFF), 45f))
                g.FillPath(fill, path);
            using (var icon = Theme.IconFont(13f))
                TextRenderer.DrawText(g, Theme.Glyph.Controller, icon, Rectangle.Round(mark), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using var font = new Font("Segoe UI Semibold", 15f);
            using var small = new Font("Segoe UI", 8f);
            TextRenderer.DrawText(g, "Joyy", font, new Point((int)mark.Right + LogicalToDeviceUnits(10), LogicalToDeviceUnits(10)), Theme.Text);
            TextRenderer.DrawText(g, "DualSense → Xbox 360", small, new Point((int)mark.Right + LogicalToDeviceUnits(12), LogicalToDeviceUnits(36)), Theme.Muted);
        }
    }
}

/// <summary>
/// Top-of-window controller chip (like DSX): status ring, controller name, profile and connection,
/// and a power button that pauses/resumes the virtual controller.
/// </summary>
internal sealed class ControllerChip : RoundCard
{
    readonly RoundIconButton _power;
    string _line1 = "No controller", _line2 = "";
    Color _ring = Theme.Idle;

    public ControllerChip(IAppActions actions)
    {
        Size = new Size(470, 56);
        Radius = 12;
        _power = new RoundIconButton(Theme.Glyph.Power) { Size = new Size(34, 34), BackColor = Theme.Card };
        _power.Click += (_, _) => actions.TogglePause();
        Controls.Add(_power);
        Layout += (_, _) => _power.Location = new Point(Width - _power.Width - LogicalToDeviceUnits(12), (Height - _power.Height) / 2);
        SetPaused(false);
    }

    public void SetPaused(bool paused)
    {
        _power.Color = paused ? Theme.Good : Theme.Bad;
        _power.AccessibleName = paused ? "Resume" : "Pause";
        _power.Invalidate();
    }

    public void Update(LiveView live, EnvView env)
    {
        var s = live.Snapshot;
        string l1 = s.Device?.Name ?? (live.HostRunning ? "Waiting for your DualSense" : "Paused");
        var parts = new List<string> { ProfileSession.NameOf(live.Active) };
        if (s.Device is { } d)
        {
            parts.Add(StatusView.Connection(d.Connection));
            if (d.Serial is { } serial)
                parts.Add(serial);
        }
        parts.Add(live.Status.Title);
        string l2 = string.Join("  ·  ", parts);
        if (l1 == _line1 && l2 == _line2 && _ring == live.Status.Color)
            return;
        (_line1, _line2, _ring) = (l1, l2, live.Status.Color);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float d = LogicalToDeviceUnits(38);
        var ring = new RectangleF(LogicalToDeviceUnits(10), (Height - d) / 2, d, d);
        using (var halo = new SolidBrush(Color.FromArgb(45, _ring)))
            g.FillEllipse(halo, ring);
        using (var pen = new Pen(_ring, 2.4f))
            g.DrawEllipse(pen, RectangleF.Inflate(ring, -2, -2));
        using (var icon = Theme.IconFont(12f))
            TextRenderer.DrawText(g, Theme.Glyph.Controller, icon, Rectangle.Round(ring), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        int x = (int)ring.Right + LogicalToDeviceUnits(12);
        int w = Width - x - LogicalToDeviceUnits(60);
        using var name = new Font("Segoe UI Semibold", 9.75f);
        using var small = new Font("Segoe UI", 8.25f);
        TextRenderer.DrawText(g, _line1, name, new Rectangle(x, LogicalToDeviceUnits(9), w, LogicalToDeviceUnits(20)), Theme.Text, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, _line2, small, new Rectangle(x, LogicalToDeviceUnits(29), w, LogicalToDeviceUnits(18)), Theme.Muted, TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Round icon button (the chip's power button).</summary>
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
