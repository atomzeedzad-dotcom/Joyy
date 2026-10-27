using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Joyy.Core;
using Joyy.Platform;

namespace Joyy.App;

// ---------------------------------------------------------------------------------------- Home

/// <summary>
/// The line under the big controller: "Cross → (A)" for the last button pressed, so you see at a
/// glance what the game receives. Shows a hint until something is pressed.
/// </summary>
internal sealed class MappingReadout : Control
{
    string _from = "";
    string _to = "";
    Color _toColor = Theme.Raised;
    bool _round;
    string _detail = "";

    public MappingReadout()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
        Height = 58;
    }

    public void Set(string from, string to, Color toColor, bool round, string detail)
    {
        if (from == _from && to == _to && toColor == _toColor && detail == _detail)
            return;
        (_from, _to, _toColor, _round, _detail) = (from, to, toColor, round, detail);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        int line1 = LogicalToDeviceUnits(30);
        using var big = new Font("Segoe UI Semibold", 13f);
        using var small = new Font("Segoe UI", 8.75f);

        if (_from.Length == 0)
        {
            TextRenderer.DrawText(g, "Press any button to see what the game gets", big, new Rectangle(0, 0, Width, line1), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            // [from]  →  (to)   centered as one group
            using var badgeFont = new Font("Segoe UI Semibold", 9f);
            var fromSize = TextRenderer.MeasureText(_from, big);
            var toText = TextRenderer.MeasureText(_to, badgeFont);
            int arrowW = LogicalToDeviceUnits(34);
            int badgeH = LogicalToDeviceUnits(24);
            int badgeW = _round ? badgeH : toText.Width + LogicalToDeviceUnits(16);
            int total = fromSize.Width + arrowW + badgeW;
            int x = (Width - total) / 2;
            TextRenderer.DrawText(g, _from, big, new Rectangle(x, 0, fromSize.Width, line1), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            x += fromSize.Width;
            using (var icon = Theme.IconFont(10f))
                TextRenderer.DrawText(g, "", icon, new Rectangle(x, 0, arrowW, line1), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += arrowW;
            var badge = new RectangleF(x, (line1 - badgeH) / 2f, badgeW, badgeH);
            using (var path = Theme.RoundedRect(badge, badgeH / 2f))
            using (var fill = new SolidBrush(_toColor))
                g.FillPath(fill, path);
            TextRenderer.DrawText(g, _to, badgeFont, Rectangle.Round(badge), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        TextRenderer.DrawText(g, _detail, small, new Rectangle(0, line1 + LogicalToDeviceUnits(4), Width, LogicalToDeviceUnits(20)), Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Home: the DualSense large in the middle (live, pressed buttons glow), what the last button
/// press sends to the game underneath, and the features as status tiles at the bottom.
/// </summary>
internal sealed class HomePage : Page
{
    static readonly Dictionary<PhysicalButton, string> Names = new()
    {
        [PhysicalButton.Cross] = "Cross", [PhysicalButton.Circle] = "Circle",
        [PhysicalButton.Square] = "Square", [PhysicalButton.Triangle] = "Triangle",
        [PhysicalButton.L1] = "L1", [PhysicalButton.R1] = "R1", [PhysicalButton.L3] = "L3", [PhysicalButton.R3] = "R3",
        [PhysicalButton.Options] = "Options", [PhysicalButton.Create] = "Create", [PhysicalButton.PS] = "PS",
        [PhysicalButton.Touchpad] = "Touchpad", [PhysicalButton.Mute] = "Mute",
        [PhysicalButton.DpadUp] = "D-pad up", [PhysicalButton.DpadDown] = "D-pad down",
        [PhysicalButton.DpadLeft] = "D-pad left", [PhysicalButton.DpadRight] = "D-pad right",
    };

    readonly PadView _pad = new(PadView.PadKind.DualSense) { Dock = DockStyle.Fill };
    readonly MappingReadout _readout = new() { Dock = DockStyle.Bottom };
    readonly Tile _tController = new(Theme.Glyph.Controller, "My controller", "");
    readonly Tile _tMapping = new(Theme.Glyph.Mapping, "Button mapping", "");
    readonly Tile _tSticks = new(Theme.Glyph.Sticks, "Sticks and triggers", "");
    readonly Tile _tProfiles = new(Theme.Glyph.Profiles, "Profiles", "");
    readonly Tile _tHide = new(Theme.Glyph.Hide, "Hide DualSense", "");
    readonly Tile _tSettings = new(Theme.Glyph.Settings, "Settings", "");
    uint _lastButtons;
    PhysicalButton? _lastPressed;
    long _lastSlow;
    bool _hadDevice;

    public HomePage(ShellContext ctx) : base(ctx, "Home", "")
    {
        _pad.BackColor = Theme.Background;

        var tiles = Grid(3, 33.3f, 33.3f, 33.4f);
        tiles.Dock = DockStyle.Bottom;
        tiles.Height = 152;
        tiles.RowCount = 2;
        tiles.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        tiles.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        void Add(Tile t, string page, int col, int row)
        {
            t.Dock = DockStyle.Fill;
            t.Margin = new Padding(col == 0 ? 0 : 5, 0, col == 2 ? 0 : 5, 8);
            t.Click += (_, _) => Ctx.Navigate(page);
            tiles.Controls.Add(t, col, row);
        }
        Add(_tController, "controller", 0, 0);
        Add(_tMapping, "mapping", 1, 0);
        Add(_tSticks, "sticks", 2, 0);
        Add(_tProfiles, "profiles", 0, 1);
        Add(_tHide, "hide", 1, 1);
        Add(_tSettings, "settings", 2, 1);

        var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border };
        var gap = new Panel { Dock = DockStyle.Bottom, Height = 14, BackColor = Color.Transparent };

        // Fill first, then the bottom-docked pieces from the bottom up.
        Controls.Add(_pad);
        Controls.Add(_readout);
        Controls.Add(gap);
        Controls.Add(divider);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 12, BackColor = Color.Transparent });
        Controls.Add(tiles);

        Ctx.SkinsChanged += () => Ctx.ApplySkins(_pad);
        Ctx.ApplySkins(_pad);
        Ctx.Session.Changed += () => UpdateEditingTiles(Ctx.Live.Active);
    }

    public override void OnLive(in LiveView live)
    {
        var s = live.Snapshot;
        bool connected = s.Running && s.Device is not null;
        _pad.SetState(s.Input, s.Output, connected);

        // Remember the most recently pressed button (or a trigger pull) for the readout.
        uint pressed = s.Input.Buttons & ~_lastButtons;
        _lastButtons = s.Input.Buttons;
        if (pressed != 0)
        {
            foreach (var b in Enum.GetValues<PhysicalButton>())
            {
                if ((pressed & PhysicalButtons.Bit(b)) != 0)
                {
                    _lastPressed = b;
                    break;
                }
            }
        }

        string detail = s.Device is { } d
            ? $"{d.Name}  ·  {StatusView.Connection(d.Connection)}"
            : live.HostRunning ? "Connect your DualSense with USB or Bluetooth" : "Paused: the virtual Xbox controller is unplugged";
        var map = ButtonMap.Create(live.Active.NintendoLayout, live.Active.ButtonOverrides);
        if (connected && s.Input.L2 > 40)
            _readout.Set("L2", "LT", Theme.Raised, false, detail);
        else if (connected && s.Input.R2 > 40)
            _readout.Set("R2", "RT", Theme.Raised, false, detail);
        else if (connected && _lastPressed is { } b)
        {
            var target = map[b];
            var (label, color, round) = Describe(target);
            _readout.Set(Names[b], label, color, round, detail);
        }
        else
            _readout.Set("", "", Theme.Raised, false, detail);

        // Poll rate jitters every frame, so the tile text refreshes a few times a second (or at once on plug/unplug).
        if (Environment.TickCount64 - _lastSlow > 400 || (s.Device is not null) != _hadDevice)
        {
            _hadDevice = s.Device is not null;
            _lastSlow = Environment.TickCount64;
            _tController.SetSubtitle(s.Device is { } dev ? $"{StatusView.Connection(dev.Connection)} · {s.PollRate:0} Hz" : "Not connected",
                s.Device is null ? Theme.Muted : null);
            UpdateEditingTiles(live.Active);
        }
    }

    void UpdateEditingTiles(Profile active)
    {
        int custom = active.ButtonOverrides.Count;
        _tMapping.SetSubtitle(custom > 0 ? $"{custom} custom button{(custom == 1 ? "" : "s")}"
            : active.NintendoLayout ? "Nintendo layout" : "Default layout");
        _tSticks.SetSubtitle($"Deadzone {active.StickDeadzone * 100:0}%  ·  rumble {active.RumbleScale * 100:0}%");
        int games = Ctx.Session.Profiles.Count - 1;
        _tProfiles.SetSubtitle($"Using {ProfileSession.NameOf(active)}  ·  {games} game profile{(games == 1 ? "" : "s")}");
    }

    public override void OnEnvironment(EnvView env)
    {
        if (!env.HidHideInstalled)
            _tHide.SetSubtitle("Needs the HidHide driver", Theme.Warn);
        else if (!env.HideEnabled)
            _tHide.SetSubtitle("Off · games may see 2 pads", Theme.Warn);
        else if (env.ReplugNeeded)
            _tHide.SetSubtitle("Replug the DualSense once", Theme.Warn);
        else if (env.HidingActive)
            _tHide.SetSubtitle("On · games see 1 pad", Theme.Good);
        else
            _tHide.SetSubtitle("On · waiting for the DualSense");
        _tSettings.SetSubtitle(env.StartWithWindows ? "Starts with Windows" : "Starts when you open it");
    }

    static (string Label, Color Color, bool Round) Describe(X360Buttons target) => target switch
    {
        X360Buttons.A => ("A", Theme.XbA, true),
        X360Buttons.B => ("B", Theme.XbB, true),
        X360Buttons.X => ("X", Theme.XbX, true),
        X360Buttons.Y => ("Y", Color.FromArgb(0xC9, 0x9A, 0x10), true),
        X360Buttons.None => ("nothing", Theme.Raised, false),
        X360Buttons.LeftShoulder => ("LB", Theme.Raised, false),
        X360Buttons.RightShoulder => ("RB", Theme.Raised, false),
        X360Buttons.LeftThumb => ("LS", Theme.Raised, false),
        X360Buttons.RightThumb => ("RS", Theme.Raised, false),
        X360Buttons.DpadUp => ("D-pad up", Theme.Raised, false),
        X360Buttons.DpadDown => ("D-pad down", Theme.Raised, false),
        X360Buttons.DpadLeft => ("D-pad left", Theme.Raised, false),
        X360Buttons.DpadRight => ("D-pad right", Theme.Raised, false),
        _ => (target.ToString(), Theme.Raised, false),
    };
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
    readonly PillButton _install = HidHideSetup.Bundled
        ? new("Install HidHide", primary: true, glyph: Theme.Glyph.Shield)
        : new("Get HidHide", primary: true, glyph: Theme.Glyph.Link);
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
        _install.Click += async (_, _) =>
        {
            if (!HidHideSetup.Bundled)
            {
                Process.Start(new ProcessStartInfo(DependencyChecker.HidHideUrl) { UseShellExecute = true })?.Dispose();
                return;
            }
            _install.Enabled = false;
            _detail.Text = "Installing HidHide… Windows will ask for permission.";
            string? error = await Ctx.Actions.InstallHidHideAsync();
            _install.Enabled = true;
            if (error is not null)
                _detail.Text = $"HidHide was not installed: {error}";
        };
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
        var logs = new ActionRow(Theme.Glyph.Log, "View log", "What Joyy did and when; daily log files are kept for 7 days");
        var photos = new ActionRow(Theme.Glyph.Folder, "Open photos folder", "Where the controller photos are kept");
        cfg.Click += (_, _) => Ctx.Actions.OpenConfigFile();
        logs.Click += (_, _) => Ctx.Navigate("log");
        photos.Click += (_, _) =>
        {
            Directory.CreateDirectory(PhotoSkin.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{PhotoSkin.Folder}\"") { UseShellExecute = true })?.Dispose();
        };
        StackTop(folders, CardTitle("Files"), logs, cfg, photos);

        var about = new RoundCard { Height = 138, Padding = new Padding(16, 10, 16, 8) };
        string version = typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        StackTop(about, CardTitle($"About Joyy {version}"),
            Body("Turns a PS5 DualSense into an Xbox 360 controller for games that only support Xbox, with buttons matched by position.", 22),
            Body("Built on SDL2 (input), ViGEmBus (virtual controller) and HidHide (device hiding, installer included; MIT license) by Nefarius Software Solutions.", 22),
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
