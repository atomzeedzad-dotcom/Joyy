using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Joyy.Core;
using Joyy.Platform;

namespace Joyy.App;

// ---------------------------------------------------------------------------------------- Home

/// <summary>Banner at the top of Home: gradient, the Xbox controller picture, name and status.</summary>
internal sealed class HeroBanner : RoundCard
{
    string _status = "";
    Color _statusColor = Theme.Idle;

    public HeroBanner()
    {
        Outline = Color.FromArgb(0x5A, 0x4A, 0x8C);
    }

    public Image? Picture { get; set; }

    public void SetStatus(string text, Color color)
    {
        if (_status == text && _statusColor == color)
            return;
        _status = text;
        _statusColor = color;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(r, LogicalToDeviceUnits(12));
        using (var fill = new LinearGradientBrush(r, Color.FromArgb(0x4B, 0x2C, 0x7E), Color.FromArgb(0x16, 0x34, 0x86), 20f))
            g.FillPath(fill, path);

        var state = g.Save();
        g.SetClip(path);
        // Soft light blobs.
        foreach (var (x, y, rad, color) in new[] { (0.78f, 0.2f, 0.55f, Color.FromArgb(70, 0x9D, 0x6B, 0xFF)), (0.95f, 0.95f, 0.5f, Color.FromArgb(60, 0x3B, 0x82, 0xF6)) })
        {
            float cx = Width * x, cy = Height * y, rr = Height * rad * 2;
            using var blob = new GraphicsPath();
            blob.AddEllipse(cx - rr, cy - rr, rr * 2, rr * 2);
            using var brush = new PathGradientBrush(blob) { CenterColor = color, SurroundColors = new[] { Color.FromArgb(0, color) } };
            g.FillPath(brush, blob);
        }
        if (Picture is { } pic)
        {
            float h = Height * 1.05f;
            float w = h * pic.Width / pic.Height;
            var dest = new RectangleF(Width - w * 0.92f, Height * 0.08f, w, h);
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(new ColorMatrix { Matrix33 = 0.92f });
            g.DrawImage(pic, Rectangle.Round(dest), 0, 0, pic.Width, pic.Height, GraphicsUnit.Pixel, attrs);
        }
        g.Restore(state);
        using (var pen = new Pen(Outline))
            g.DrawPath(pen, path);

        int pad = LogicalToDeviceUnits(22);
        var badge = new RectangleF(pad, Height - LogicalToDeviceUnits(84), LogicalToDeviceUnits(46), LogicalToDeviceUnits(46));
        using (var badgePath = Theme.RoundedRect(badge, LogicalToDeviceUnits(10)))
        using (var badgeFill = new SolidBrush(Color.FromArgb(200, 0x1A, 0x14, 0x2C)))
        using (var badgePen = new Pen(Color.FromArgb(150, Theme.Accent), 1.5f))
        {
            g.FillPath(badgeFill, badgePath);
            g.DrawPath(badgePen, badgePath);
        }
        using (var icon = Theme.IconFont(16f))
            TextRenderer.DrawText(g, Theme.Glyph.Controller, icon, Rectangle.Round(badge), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        int tx = (int)badge.Right + LogicalToDeviceUnits(14);
        using var big = new Font("Segoe UI Semibold", 20f);
        using var tag = new Font("Segoe UI", 9.5f);
        TextRenderer.DrawText(g, "Joyy", big, new Point(tx, (int)badge.Y - LogicalToDeviceUnits(12)), Color.White);
        TextRenderer.DrawText(g, "Play Xbox-only games with your DualSense.", tag, new Point(tx + 2, (int)badge.Y + LogicalToDeviceUnits(26)), Color.FromArgb(0xE0, 0xD8, 0xF5));

        if (_status.Length > 0)
        {
            using var chipFont = new Font("Segoe UI Semibold", 8.75f);
            var size = TextRenderer.MeasureText(_status, chipFont);
            var chip = new RectangleF(pad, pad, size.Width + LogicalToDeviceUnits(34), LogicalToDeviceUnits(28));
            using (var chipPath = Theme.RoundedRect(chip, chip.Height / 2))
            using (var chipFill = new SolidBrush(Color.FromArgb(170, 0x14, 0x10, 0x24)))
                g.FillPath(chipFill, chipPath);
            using (var dot = new SolidBrush(_statusColor))
                g.FillEllipse(dot, chip.X + LogicalToDeviceUnits(11), chip.Y + chip.Height / 2 - LogicalToDeviceUnits(4), LogicalToDeviceUnits(8), LogicalToDeviceUnits(8));
            TextRenderer.DrawText(g, _status, chipFont, new Rectangle((int)chip.X + LogicalToDeviceUnits(25), (int)chip.Y, size.Width + 6, (int)chip.Height), Color.White, TextFormatFlags.VerticalCenter);
        }
    }
}

internal sealed class HomePage : Page
{
    readonly Label _name = new() { AutoSize = false, Height = 24, ForeColor = Theme.Text, BackColor = Theme.Card, Font = new Font("Segoe UI Semibold", 10.5f) };
    readonly Label _serial = new() { AutoSize = false, Height = 20, ForeColor = Theme.Muted, BackColor = Theme.Card, Font = new Font("Segoe UI", 8.5f) };
    readonly PadView _pad = new(PadView.PadKind.DualSense) { Height = 200 };
    readonly Segmented _virtual = new("Off", "Xbox 360");
    readonly ActionRow _hideRow;
    readonly ToggleSwitch _hide;
    readonly ActionRow _profileRow = new(Theme.Glyph.Profiles, "Active profile", "", chevron: true);
    readonly ActionRow _rumbleRow = new(Theme.Glyph.Vibrate, "Test rumble", "Buzz the DualSense", chevron: false);
    readonly HeroBanner _hero = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
    bool _loading;

    public HomePage(ShellContext ctx) : base(ctx, "Home", "Overview and quick actions")
    {
        (_hideRow, _hide) = ToggleRow(Theme.Glyph.Hide, "Hide DualSense from games", "Only Joyy can see it");

        var grid = Grid(2, -360, 100);
        grid.RowCount = 1;
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new RoundCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0), Padding = new Padding(16, 12, 16, 10) };
        _pad.BackColor = Theme.Card;
        StackTop(left, _name, _serial, _pad, Spacer(6), Caption("Virtual device"), _virtual, Spacer(10),
            Caption("Quick actions"), _hideRow, _profileRow, _rumbleRow);
        grid.Controls.Add(left, 0, 0);

        var right = Grid(1, 100);
        right.RowCount = 2;
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.Controls.Add(_hero, 0, 0);

        var tiles = Grid(3, 33.3f, 33.3f, 33.4f);
        tiles.RowCount = 3;
        tiles.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        tiles.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // takes the leftover height so tiles keep their size
        void AddTile(string glyph, string title, string sub, string page, int col, int row)
        {
            var t = new Tile(glyph, title, sub) { Dock = DockStyle.Fill };
            t.Click += (_, _) => Ctx.Navigate(page);
            tiles.Controls.Add(t, col, row);
        }
        AddTile(Theme.Glyph.Controller, "My Controller", "Connection and device details", "controller", 0, 0);
        AddTile(Theme.Glyph.Profiles, "Profiles", "All games and per-game profiles", "profiles", 1, 0);
        AddTile(Theme.Glyph.Mapping, "Button Mapping", "Choose what each button sends", "mapping", 2, 0);
        AddTile(Theme.Glyph.Sticks, "Sticks & Triggers", "Deadzones, curves and rumble", "sticks", 0, 1);
        AddTile(Theme.Glyph.Hide, "Hide DualSense", "Stop double controllers", "hide", 1, 1);
        AddTile(Theme.Glyph.Log, "Log", "What Joyy has been doing", "log", 2, 1);
        right.Controls.Add(tiles, 0, 1);
        grid.Controls.Add(right, 1, 0);
        Controls.Add(grid);

        _virtual.SelectionChanged += i =>
        {
            if (!_loading)
                Ctx.Actions.SetVirtualDevice(i == 1);
        };
        _hide.CheckedChanged += (_, _) =>
        {
            if (!_loading)
                Ctx.Actions.SetHideDualSense(_hide.Checked);
        };
        _profileRow.Click += (_, _) => Ctx.Navigate("profiles");
        _rumbleRow.Click += (_, _) => Ctx.Actions.TestRumble(Ctx.Live.Active.RumbleScale);
        Ctx.SkinsChanged += ApplySkins;
        ApplySkins();
    }

    void ApplySkins()
    {
        Ctx.ApplySkins(_pad);
        _hero.Picture = Ctx.XboxSkin?.Image;
        _hero.Invalidate();
    }

    public override void OnLive(in LiveView live)
    {
        var s = live.Snapshot;
        _pad.SetState(s.Input, s.Output, s.Running && s.Device is not null);
        _name.Text = s.Device?.Name ?? "No controller connected";
        _serial.Text = s.Device is { } d ? $"{StatusView.Connection(d.Connection)}  ·  {d.Serial ?? "no serial"}" : "Connect a DualSense with USB or Bluetooth";
        _hero.SetStatus(live.Status.Title, live.Status.Color);
        string profile = ProfileSession.NameOf(live.Active);
        if (_profileRow.Value != profile)
        {
            _profileRow.Value = profile;
            _profileRow.Invalidate();
        }
        _loading = true;
        _virtual.Selected = live.HostRunning ? 1 : 0;
        _loading = false;
    }

    public override void OnEnvironment(EnvView env)
    {
        _loading = true;
        _hide.SetSilently(env.HideEnabled);
        _hideRow.Enabled = env.HidHideInstalled;
        _hideRow.SetContent(Theme.Glyph.Hide, "Hide DualSense from games",
            !env.HidHideInstalled ? "Needs the HidHide driver" : env.ReplugNeeded ? "Replug the DualSense once" : env.HidingActive ? "Hidden: games see only Xbox" : "Only Joyy can see it");
        _loading = false;
    }
}

// ---------------------------------------------------------------------------------------- My Controller

internal sealed class ControllerPage : Page
{
    readonly Label _name = new() { AutoSize = false, Height = 30, ForeColor = Theme.Text, BackColor = Theme.Card, Font = new Font("Segoe UI Semibold", 13f) };
    readonly Label _kind = new() { AutoSize = false, Height = 20, ForeColor = Theme.Muted, BackColor = Theme.Card, Font = new Font("Segoe UI", 9f) };
    readonly Chip _connection = new(Theme.Glyph.Usb, "USB", Theme.Good);
    readonly Chip _virtual = new(Theme.Glyph.Controller, "Xbox 360", Theme.Good);
    readonly Chip _hidden = new(Theme.Glyph.Hide, "Hidden", Theme.Good);
    readonly InfoTile _tName = new(Theme.Glyph.Controller, "Name");
    readonly InfoTile _tType = new(Theme.Glyph.Game, "Controller type");
    readonly InfoTile _tConn = new(Theme.Glyph.Usb, "Connection type");
    readonly InfoTile _tVidPid = new(Theme.Glyph.Info, "VID / PID");
    readonly InfoTile _tSerial = new(Theme.Glyph.Link, "Serial (MAC)");
    readonly InfoTile _tRate = new(Theme.Glyph.Refresh, "Polling rate");
    readonly InfoTile _tSlot = new(Theme.Glyph.Controller, "Virtual Xbox 360");
    readonly InfoTile _tProfile = new(Theme.Glyph.Profiles, "Active profile");
    readonly InfoTile _tDriver = new(Theme.Glyph.Settings, "Input driver");
    readonly CheckLine _cViGEm = new("ViGEmBus driver");
    readonly CheckLine _cVirtual = new("Virtual Xbox controller");
    readonly CheckLine _cHidHide = new("HidHide driver");
    readonly CheckLine _cHidden = new("DualSense hidden from games");
    readonly CheckLine _cOthers = new("Other controller tools");
    long _lastSlow;

    public ControllerPage(ShellContext ctx) : base(ctx, "My Controller", "Connection and device details")
    {
        var header = new RoundCard { Height = 110, Padding = new Padding(20, 14, 20, 12) };
        var chips = new FlowLayoutPanel { Height = 32, BackColor = Theme.Card, WrapContents = false, Margin = Padding.Empty };
        chips.Controls.AddRange(new Control[] { _connection, _virtual, _hidden });
        StackTop(header, _name, _kind, Spacer(8), chips);

        var info = new RoundCard { Height = 262, Padding = new Padding(16, 12, 16, 12) };
        var tiles = new TableLayoutPanel { ColumnCount = 3, RowCount = 3, BackColor = Theme.Card, Height = 180 };
        for (int i = 0; i < 3; i++)
        {
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            tiles.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        }
        tiles.Controls.AddRange(new Control[] { _tName, _tType, _tConn, _tVidPid, _tSerial, _tRate, _tSlot, _tProfile, _tDriver });
        StackTop(info, CardTitle("Device information"), Body("Live details of the DualSense and the virtual controller."), Spacer(6), tiles);

        var health = new RoundCard { Height = 290, Padding = new Padding(16, 12, 16, 8) };
        StackTop(health, CardTitle("Health"), Spacer(4), _cViGEm, _cVirtual, _cHidHide, _cHidden, _cOthers);

        StackTop(this, header, Spacer(12, Color.Transparent), info, Spacer(12, Color.Transparent), health);
        _tDriver.Value = $"SDL {SdlPad.NativeVersion} · HIDAPI";
    }

    public override void OnLive(in LiveView live)
    {
        var s = live.Snapshot;
        var d = s.Device;
        _name.Text = d?.Name ?? "No controller connected";
        _kind.Text = d is null ? "Connect a DualSense with a USB cable or Bluetooth."
            : d.ProductId == DeviceIds.DualSenseEdge ? "Sony DualSense Edge" : "Sony DualSense";
        _connection.Set(d?.Connection == ConnectionKind.Bluetooth ? Theme.Glyph.Bluetooth : Theme.Glyph.Usb,
            d is null ? "Disconnected" : StatusView.Connection(d.Connection), d is null ? Theme.Idle : Theme.Good);
        bool virt = s.Running && s.Status == BridgeStatus.Running;
        _virtual.Set(Theme.Glyph.Controller, virt ? $"Xbox 360{(s.VirtualSlot is int i ? $" · slot {i + 1}" : "")}" : "No virtual pad", virt ? Theme.Good : Theme.Idle);

        _tName.Value = d?.Name ?? "—";
        _tType.Value = d is null ? "—" : d.ProductId == DeviceIds.DualSenseEdge ? "DualSense Edge" : "DualSense";
        _tConn.Value = d is null ? "—" : StatusView.Connection(d.Connection);
        _tVidPid.Value = d is null ? "—" : $"0x{d.VendorId:X4} / 0x{d.ProductId:X4}";
        _tSerial.Value = d?.Serial ?? "—";
        _tSlot.Value = virt ? $"Plugged in{(s.VirtualSlot is int slot ? $", XInput slot {slot + 1}" : "")}" : "Not plugged in";
        _tProfile.Value = ProfileSession.NameOf(live.Active);

        if (Environment.TickCount64 - _lastSlow > 500)
        {
            _lastSlow = Environment.TickCount64;
            _tRate.Value = s.Running ? $"{s.PollRate:0} Hz" : "—";
            _cVirtual.Set(virt ? CheckLine.State.Ok : s.Status == BridgeStatus.VirtualPadUnavailable ? CheckLine.State.Bad : CheckLine.State.Off,
                virt ? "Games see an Xbox 360 controller" : s.Status == BridgeStatus.VirtualPadUnavailable ? s.Detail ?? "Unavailable" : "Waiting for a DualSense (or paused)");
        }
    }

    public override void OnEnvironment(EnvView env)
    {
        _hidden.Set(Theme.Glyph.Hide, env.HidingActive && !env.ReplugNeeded ? "Hidden from games" : "Visible to games",
            env.HidingActive && !env.ReplugNeeded ? Theme.Good : Theme.Warn);
        _cViGEm.Set(env.ViGEmInstalled ? CheckLine.State.Ok : CheckLine.State.Bad,
            env.ViGEmInstalled ? "Installed: creates the virtual Xbox 360 controller" : "Not installed: no virtual controller possible");
        _cHidHide.Set(env.HidHideInstalled ? CheckLine.State.Ok : CheckLine.State.Warn,
            env.HidHideInstalled ? "Installed" : "Not installed: games may see two controllers");
        _cHidden.Set(env.HidingActive && !env.ReplugNeeded ? CheckLine.State.Ok : env.ReplugNeeded ? CheckLine.State.Warn : CheckLine.State.Off,
            env.ReplugNeeded ? "Unplug and replug the DualSense once" : env.HidingActive ? "Only Joyy can see the DualSense" : "Off");
        _cOthers.Set(env.Conflicts.Count == 0 ? CheckLine.State.Ok : CheckLine.State.Warn,
            env.Conflicts.Count == 0 ? "None running" : string.Join(", ", env.Conflicts) + " may also use the DualSense");
    }
}

// ---------------------------------------------------------------------------------------- Hide DualSense

internal sealed class HidePage : Page
{
    readonly Label _state = new() { AutoSize = false, Height = 30, ForeColor = Theme.Text, BackColor = Theme.Card, Font = new Font("Segoe UI Semibold", 12.5f) };
    readonly Label _detail = new() { AutoSize = false, Height = 40, ForeColor = Theme.Muted, BackColor = Theme.Card, Font = new Font("Segoe UI", 9f) };
    readonly ActionRow _toggleRow;
    readonly ToggleSwitch _toggle;
    readonly CheckLine _driver = new("HidHide driver");
    readonly CheckLine _attached = new("Attached to your DualSense");
    readonly CheckLine _hidden = new("Games see only the Xbox controller");
    readonly PillButton _install = new("Install HidHide", primary: true, glyph: Theme.Glyph.Link);
    bool _loading;

    public HidePage(ShellContext ctx) : base(ctx, "Hide DualSense", "Stop games from seeing two controllers")
    {
        (_toggleRow, _toggle) = ToggleRow(Theme.Glyph.Hide, "Hide the DualSense from games", "Joyy keeps reading it; turned off automatically on Pause and Exit");

        var top = new RoundCard { Height = 150, Padding = new Padding(20, 14, 20, 10) };
        _install.VisibleChanged += (_, _) => top.Height = top.LogicalToDeviceUnits(_install.Visible ? 196 : 150);
        var buttons = new FlowLayoutPanel { Height = 38, BackColor = Theme.Card, WrapContents = false };
        _install.BackColor = Theme.Card;
        buttons.Controls.Add(_install);
        StackTop(top, _state, _detail, _toggleRow, buttons);

        var checks = new RoundCard { Height = 196, Padding = new Padding(16, 12, 16, 8) };
        StackTop(checks, CardTitle("Checks"), _driver, _attached, _hidden);

        var help = new RoundCard { Height = 170, Padding = new Padding(16, 12, 16, 12) };
        StackTop(help, CardTitle("Good to know"),
            Body("• Start Joyy before the game. A game that is already open keeps the controllers it found at start.", 22),
            Body("• The first time after installing HidHide, unplug and replug the DualSense (or restart Windows).", 22),
            Body("• Joyy only undoes what it changed; your own HidHide settings are left alone.", 22),
            Body("• Emergency fix if the controller ever stays invisible:  Joyy.exe --restore", 22));

        StackTop(this, top, Spacer(12, Color.Transparent), checks, Spacer(12, Color.Transparent), help);

        _toggle.CheckedChanged += (_, _) =>
        {
            if (!_loading)
                Ctx.Actions.SetHideDualSense(_toggle.Checked);
        };
        _install.Click += (_, _) => Process.Start(new ProcessStartInfo(DependencyChecker.HidHideUrl) { UseShellExecute = true })?.Dispose();
    }

    public override void OnEnvironment(EnvView env)
    {
        _loading = true;
        _toggle.SetSilently(env.HideEnabled);
        _toggleRow.Enabled = env.HidHideInstalled;
        _loading = false;
        _install.Visible = !env.HidHideInstalled;

        bool good = env.HidingActive && !env.ReplugNeeded;
        _state.Text = !env.HidHideInstalled ? "HidHide is not installed"
            : !env.HideEnabled ? "Hiding is off"
            : env.ReplugNeeded ? "Almost done"
            : good ? "Your DualSense is hidden" : "Waiting for a DualSense";
        _state.ForeColor = good ? Theme.Good : env.ReplugNeeded || !env.HidHideInstalled ? Theme.Warn : Theme.Text;
        _detail.Text = env.Hiding.Text;

        _driver.Set(env.HidHideInstalled ? CheckLine.State.Ok : CheckLine.State.Bad, env.HidHideInstalled ? "Installed" : "Not installed");
        _attached.Set(!env.HidHideInstalled ? CheckLine.State.Off : env.ReplugNeeded ? CheckLine.State.Warn : CheckLine.State.Ok,
            env.ReplugNeeded ? "Unplug and replug the DualSense once" : env.HidHideInstalled ? "Ready" : "—");
        _hidden.Set(good ? CheckLine.State.Ok : CheckLine.State.Off, good ? "Only Joyy can see the DualSense" : "Not active");
    }
}

// ---------------------------------------------------------------------------------------- Log

internal sealed class LogPage : Page
{
    const int MaxLines = 500;
    readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, Font = new Font("Cascadia Mono", 9f),
    };
    readonly List<string> _lines = new();

    public LogPage(ShellContext ctx) : base(ctx, "Log", "What Joyy has been doing")
    {
        var card = new RoundCard { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 10, 12) };
        card.Controls.Add(_log);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, BackColor = Color.Transparent, Padding = new Padding(0, 10, 0, 0) };
        var logs = new PillButton("Open log folder", glyph: Theme.Glyph.Folder) { BackColor = Color.Transparent };
        var config = new PillButton("Open config.json", glyph: Theme.Glyph.Edit) { BackColor = Color.Transparent };
        logs.Click += (_, _) => Ctx.Actions.OpenLogFolder();
        config.Click += (_, _) => Ctx.Actions.OpenConfigFile();
        buttons.Controls.Add(logs);
        buttons.Controls.Add(config);
        Controls.Add(card);
        Controls.Add(buttons);
        Theme.DarkScrollbars(_log);
    }

    public void Append(DateTime time, HostEventKind kind, string message)
    {
        _lines.Add($"{time:HH:mm:ss}  {kind,-7}  {message}");
        if (_lines.Count > MaxLines)
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        _log.Lines = _lines.ToArray();
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}

// ---------------------------------------------------------------------------------------- Settings

internal sealed class SettingsPage : Page
{
    readonly ToggleSwitch _startWithWindows;
    readonly ToggleSwitch _startHidden;
    readonly ActionRow _dsPicture = new(Theme.Glyph.Picture, "DualSense picture", "", chevron: true);
    readonly ActionRow _xbPicture = new(Theme.Glyph.Picture, "Xbox controller picture", "", chevron: true);
    bool _loading;

    public SettingsPage(ShellContext ctx) : base(ctx, "Settings", "Startup, pictures and about")
    {
        ActionRow startRow, hiddenRow;
        (startRow, _startWithWindows) = ToggleRow(Theme.Glyph.Power, "Start Joyy with Windows", "Runs in the tray when you sign in");
        (hiddenRow, _startHidden) = ToggleRow(Theme.Glyph.Hide, "Start hidden in the tray", "Don't show this window at startup");

        var general = new RoundCard { Height = 150, Padding = new Padding(16, 10, 16, 6) };
        StackTop(general, CardTitle("Startup"), startRow, hiddenRow);

        var pictures = new RoundCard { Height = 150, Padding = new Padding(16, 10, 16, 6) };
        StackTop(pictures, CardTitle("Controller pictures"), _dsPicture, _xbPicture);

        var folders = new RoundCard { Height = 204, Padding = new Padding(16, 10, 16, 6) };
        var cfg = new ActionRow(Theme.Glyph.Edit, "Open config.json", "Profiles and settings (edits are picked up live)");
        var logs = new ActionRow(Theme.Glyph.Folder, "Open log folder", "Daily logs, kept for 7 days");
        var photos = new ActionRow(Theme.Glyph.Folder, "Open photos folder", "Where the controller photos are kept");
        cfg.Click += (_, _) => Ctx.Actions.OpenConfigFile();
        logs.Click += (_, _) => Ctx.Actions.OpenLogFolder();
        photos.Click += (_, _) =>
        {
            Directory.CreateDirectory(PhotoSkin.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{PhotoSkin.Folder}\"") { UseShellExecute = true })?.Dispose();
        };
        StackTop(folders, CardTitle("Files"), cfg, logs, photos);

        var about = new RoundCard { Height = 138, Padding = new Padding(16, 10, 16, 8) };
        string version = typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        StackTop(about, CardTitle($"About Joyy {version}"),
            Body("Turns a PS5 DualSense into an Xbox 360 controller for games that only support Xbox, with buttons matched by position.", 22),
            Body("Built on SDL2 (input), ViGEmBus (virtual controller) and HidHide (device hiding) by Nefarius Software Solutions.", 22),
            Body("Controller photos are the makers' product images, used locally for display only.", 22));

        StackTop(this, general, Spacer(10, Color.Transparent), pictures, Spacer(10, Color.Transparent), folders, Spacer(10, Color.Transparent), about);

        _startWithWindows.CheckedChanged += (_, _) => { if (!_loading) Ctx.Actions.SetStartWithWindows(_startWithWindows.Checked); };
        _startHidden.CheckedChanged += (_, _) => { if (!_loading) Ctx.Actions.SetStartMinimized(_startHidden.Checked); };
        _dsPicture.Click += (_, _) => PictureMenu(PadView.PadKind.DualSense, _dsPicture);
        _xbPicture.Click += (_, _) => PictureMenu(PadView.PadKind.Xbox, _xbPicture);
        Ctx.SkinsChanged += DescribePictures;
        DescribePictures();
    }

    void DescribePictures()
    {
        _dsPicture.Value = Ctx.DualSenseSkin is null ? "Built-in drawing" : "Photo";
        _xbPicture.Value = Ctx.XboxSkin is null ? "Built-in drawing" : "Photo";
        _dsPicture.Invalidate();
        _xbPicture.Invalidate();
    }

    void PictureMenu(PadView.PadKind kind, Control anchor)
    {
        var menu = new ContextMenuStrip { Renderer = new Theme.MenuRenderer(), BackColor = Theme.Card, ForeColor = Theme.Text };
        menu.Items.Add("Use a photo…", null, (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = kind == PadView.PadKind.DualSense ? "Choose a DualSense photo" : "Choose an Xbox controller photo",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                return;
            try
            {
                var skin = PhotoSkin.Install(kind, dialog.FileName);
                Replace(kind, skin);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), $"Could not use that picture: {ex.Message}", "Joyy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        });
        menu.Items.Add("Use the built-in drawing", null, (_, _) =>
        {
            PhotoSkin.Remove(kind);
            Replace(kind, null);
        });
        menu.Show(anchor, new Point(anchor.Width - 220, anchor.Height));
    }

    void Replace(PadView.PadKind kind, PhotoSkin? skin)
    {
        var old = kind == PadView.PadKind.DualSense ? Ctx.DualSenseSkin : Ctx.XboxSkin;
        if (kind == PadView.PadKind.DualSense)
            Ctx.DualSenseSkin = skin;
        else
            Ctx.XboxSkin = skin;
        Ctx.RaiseSkinsChanged();
        old?.Dispose();
    }

    public override void OnEnvironment(EnvView env)
    {
        _loading = true;
        _startWithWindows.SetSilently(env.StartWithWindows);
        _startHidden.SetSilently(env.StartMinimized);
        _loading = false;
    }
}
