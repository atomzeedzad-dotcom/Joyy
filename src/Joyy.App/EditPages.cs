using System.Drawing.Drawing2D;
using Joyy.Core;

namespace Joyy.App;

// ---------------------------------------------------------------------------------------- Profiles

/// <summary>One profile as a card: icon, name, when it applies, and Edit / Delete.</summary>
internal sealed class ProfileCard : OwnerButton
{
    public ProfileCard(Profile profile, bool isGlobal, bool editing, bool active)
    {
        Profile = profile;
        IsGlobal = isGlobal;
        Editing = editing;
        Active = active;
        BackColor = Color.Transparent;
        Text = ProfileSession.NameOf(profile);
        AccessibleName = Text;
        Font = new Font("Segoe UI Semibold", 10f);
        Size = new Size(320, 92);
        Margin = new Padding(0, 0, 12, 12);
    }

    public Profile Profile { get; }
    public bool IsGlobal { get; }
    public bool Editing { get; }
    public bool Active { get; }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundedRect(r, Px(10)))
        using (var fill = new SolidBrush(Hovered ? Theme.Hover : Theme.Card))
        using (var pen = new Pen(Editing ? Theme.Accent : Theme.Border, Editing ? 1.8f : 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        var tile = new RectangleF(Px(14), Px(16), Px(44), Px(44));
        FillRounded(g, tile, Px(8), Theme.Raised);
        DrawGlyph(g, IsGlobal ? Theme.Glyph.Home : Theme.Glyph.Game, Rectangle.Round(tile), Theme.Text, 15f);

        using var small = new Font("Segoe UI", 8.25f);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Px(70), Px(14), Width - Px(80), Px(22)), Theme.Text, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, IsGlobal ? "Default for every game" : $"{ProfileSelector.NormalizeProcessName(Profile.ProcessName)}.exe",
            small, new Rectangle(Px(70), Px(36), Width - Px(80), Px(18)), Theme.Muted, TextFormatFlags.EndEllipsis);

        int x = Px(70);
        void Badge(string text, Color color)
        {
            var size = TextRenderer.MeasureText(text, small);
            var b = new RectangleF(x, Px(60), size.Width + Px(14), Px(20));
            using var path = Theme.RoundedRect(b, b.Height / 2);
            using var fill = new SolidBrush(Color.FromArgb(50, color));
            g.FillPath(fill, path);
            TextRenderer.DrawText(g, text, small, Rectangle.Round(b), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += (int)b.Width + Px(6);
        }
        if (Active)
            Badge("Active now", Theme.Good);
        if (Editing)
            Badge("Editing", Theme.Accent);
        if (Profile.NintendoLayout)
            Badge("Nintendo layout", Theme.Muted);
        DrawGlyph(g, Theme.Glyph.Chevron, new Rectangle(Width - Px(30), 0, Px(20), Height), Theme.Muted, 8f);
    }
}

internal sealed class ProfilesPage : Page
{
    readonly FlowLayoutPanel _cards = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent, AutoScroll = true, Padding = Padding.Empty };
    string? _activeName;

    public ProfilesPage(ShellContext ctx) : base(ctx, "Profiles", "All games, or one profile per game")
    {
        var actions = new RoundCard { Height = 140, Padding = new Padding(16, 10, 16, 8) };
        var add = new ActionRow(Theme.Glyph.Add, "Create a game profile", "Pick a running game (or type its .exe); it's used automatically while that game is in front");
        var remove = new ActionRow(Theme.Glyph.Delete, "Delete the profile being edited", "Game profiles only; press Save to make it permanent", chevron: false);
        StackTop(actions, add, remove);
        add.Click += (_, _) => AddGame();
        remove.Click += (_, _) =>
        {
            if (Ctx.Session.Current > 0)
                Ctx.Session.Remove(Ctx.Session.Current);
        };

        var header = new EditHeader(ctx.Session);
        Controls.Add(_cards);
        StackTop(this, header, Spacer(6, Color.Transparent), actions, Spacer(14, Color.Transparent));

        Ctx.Session.Changed += Rebuild;
        Rebuild();
    }

    void AddGame()
    {
        using var dialog = new AddGameDialog();
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;
        if (Ctx.Session.AddGame(dialog.DisplayName, dialog.ProcessName) is { } error)
            MessageBox.Show(FindForm(), error, "Joyy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    void Rebuild()
    {
        _cards.SuspendLayout();
        foreach (Control c in _cards.Controls)
            c.Dispose();
        _cards.Controls.Clear();
        for (int i = 0; i < Ctx.Session.Profiles.Count; i++)
        {
            int index = i;
            var p = Ctx.Session.Profiles[i];
            var card = new ProfileCard(p, i == 0, i == Ctx.Session.Current, ProfileSession.NameOf(p) == _activeName);
            card.Click += (_, _) =>
            {
                Ctx.Session.Select(index);
                Ctx.Navigate("mapping");
            };
            _cards.Controls.Add(card);
        }
        _cards.ResumeLayout();
    }

    public override void OnLive(in LiveView live)
    {
        string name = ProfileSession.NameOf(live.Active);
        if (name != _activeName)
        {
            _activeName = name;
            Rebuild();
        }
    }
}

// ---------------------------------------------------------------------------------------- Button mapping

internal sealed class MappingPage : Page
{
    static readonly (string Label, X360Buttons Value)[] Targets =
    {
        ("None", X360Buttons.None),
        ("A", X360Buttons.A), ("B", X360Buttons.B), ("X", X360Buttons.X), ("Y", X360Buttons.Y),
        ("LB", X360Buttons.LeftShoulder), ("RB", X360Buttons.RightShoulder),
        ("LS (left stick click)", X360Buttons.LeftThumb), ("RS (right stick click)", X360Buttons.RightThumb),
        ("Start", X360Buttons.Start), ("Back", X360Buttons.Back), ("Guide", X360Buttons.Guide),
        ("D-pad Up", X360Buttons.DpadUp), ("D-pad Down", X360Buttons.DpadDown),
        ("D-pad Left", X360Buttons.DpadLeft), ("D-pad Right", X360Buttons.DpadRight),
    };

    static readonly Dictionary<PhysicalButton, string> Names = new()
    {
        [PhysicalButton.Cross] = "Cross", [PhysicalButton.Circle] = "Circle",
        [PhysicalButton.Square] = "Square", [PhysicalButton.Triangle] = "Triangle",
        [PhysicalButton.L1] = "L1", [PhysicalButton.R1] = "R1",
        [PhysicalButton.L3] = "L3 (left stick click)", [PhysicalButton.R3] = "R3 (right stick click)",
        [PhysicalButton.Options] = "Options", [PhysicalButton.Create] = "Create",
        [PhysicalButton.PS] = "PS button", [PhysicalButton.Touchpad] = "Touchpad click",
        [PhysicalButton.Mute] = "Mute",
        [PhysicalButton.DpadUp] = "D-pad Up", [PhysicalButton.DpadDown] = "D-pad Down",
        [PhysicalButton.DpadLeft] = "D-pad Left", [PhysicalButton.DpadRight] = "D-pad Right",
    };

    readonly PadView _pad = new(PadView.PadKind.DualSense) { Clickable = true, ShowDisconnectedVeil = false };
    readonly Segmented _view = new("PHYSICAL", "VIRTUAL") { Width = 220 };
    readonly Label _selectedTitle = new() { AutoSize = false, Height = 30, ForeColor = Theme.Text, BackColor = Theme.Card, Font = new Font("Segoe UI Semibold", 12f), TextAlign = ContentAlignment.MiddleCenter };
    readonly Label _selectedHint = new() { AutoSize = false, Height = 20, ForeColor = Theme.Muted, BackColor = Theme.Card, Font = new Font("Segoe UI", 8.75f), TextAlign = ContentAlignment.MiddleCenter, Text = "Click a button on the picture, or press it on your controller" };
    readonly Panel _list = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Card };
    readonly Dictionary<PhysicalButton, ActionRow> _rows = new();
    readonly ToggleSwitch _nintendo;
    readonly ActionRow _nintendoRow;
    PhysicalButton _selected = PhysicalButton.Cross;
    uint _lastButtons;
    bool _loading;

    public MappingPage(ShellContext ctx) : base(ctx, "Button Mapping", "Choose what each DualSense button sends to the game")
    {
        (_nintendoRow, _nintendo) = ToggleRow(Theme.Glyph.Mapping, "Nintendo layout", "Swap A/B and X/Y");

        var grid = Grid(2, 46, 54);
        grid.RowCount = 1;
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new RoundCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0), Padding = new Padding(16, 12, 16, 12) };
        var viewRow = new Panel { Height = 40, BackColor = Theme.Card };
        var viewLabel = new Label { Text = "Input view", AutoSize = true, ForeColor = Theme.Muted, BackColor = Theme.Card, Location = new Point(0, 10), Font = new Font("Segoe UI", 9f) };
        viewRow.Controls.Add(_view);
        viewRow.Controls.Add(viewLabel);
        viewRow.Layout += (_, _) => _view.Location = new Point(viewRow.Width - _view.Width, 2);
        var selectedBox = new RoundCard { Height = 76, Fill = Theme.Raised, Outline = Theme.Border, Padding = new Padding(10, 8, 10, 8) };
        _selectedTitle.BackColor = Theme.Raised;
        _selectedHint.BackColor = Theme.Raised;
        StackTop(selectedBox, _selectedTitle, _selectedHint);
        _pad.Dock = DockStyle.Fill;
        left.Controls.Add(_pad);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 84, BackColor = Theme.Card, Padding = new Padding(0, 8, 0, 0) };
        bottom.Controls.Add(selectedBox);
        selectedBox.Dock = DockStyle.Fill;
        left.Controls.Add(bottom);
        viewRow.Dock = DockStyle.Top;
        left.Controls.Add(viewRow);
        grid.Controls.Add(left, 0, 0);

        var right = new RoundCard { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 8, 10) };
        right.Controls.Add(_list);
        var listHeader = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Card };
        StackTop(listHeader, CardTitle("Buttons"), _nintendoRow);
        right.Controls.Add(listHeader);
        grid.Controls.Add(right, 1, 0);

        Controls.Add(grid);
        StackTop(this, new EditHeader(ctx.Session), Spacer(8, Color.Transparent));

        // Rows, top to bottom in enum order.
        var buttons = Enum.GetValues<PhysicalButton>();
        for (int i = buttons.Length - 1; i >= 0; i--)
        {
            var b = buttons[i];
            var row = new ActionRow(Theme.Glyph.Controller, Names[b], "", chevron: true) { Height = 44, Dock = DockStyle.Top };
            row.Click += (_, _) => { Select(b); ShowTargets(b, row); };
            _rows[b] = row;
            _list.Controls.Add(row);
        }
        Theme.DarkScrollbars(_list);

        _view.SelectionChanged += i =>
        {
            _pad.Kind = i == 0 ? PadView.PadKind.DualSense : PadView.PadKind.Xbox;
            UpdateSelection();
        };
        _pad.PhysicalButtonClicked += b => { Select(b); ShowTargets(b, _rows[b]); };
        _pad.XboxButtonClicked += x =>
        {
            // Select the first DualSense button that currently sends this Xbox button.
            var map = Map();
            foreach (var b in Enum.GetValues<PhysicalButton>())
            {
                if ((map[b] & x) != 0)
                {
                    Select(b);
                    return;
                }
            }
        };
        _nintendo.CheckedChanged += (_, _) =>
        {
            if (!_loading)
                Ctx.Session.Update(p => p with { NintendoLayout = _nintendo.Checked });
        };
        Ctx.Session.Changed += RefreshRows;
        Ctx.SkinsChanged += () => Ctx.ApplySkins(_pad);
        Ctx.ApplySkins(_pad);
        RefreshRows();
    }

    ButtonMap Map() => ButtonMap.Create(Ctx.Session.Editing.NintendoLayout, Ctx.Session.Editing.ButtonOverrides);

    static string Label(X360Buttons value) => Targets.FirstOrDefault(t => t.Value == value).Label ?? value.ToString();

    void RefreshRows()
    {
        _loading = true;
        _nintendo.SetSilently(Ctx.Session.Editing.NintendoLayout);
        _loading = false;
        var map = Map();
        var defaults = ButtonMap.Create(Ctx.Session.Editing.NintendoLayout);
        foreach (var (b, row) in _rows)
        {
            bool custom = Ctx.Session.Editing.ButtonOverrides.ContainsKey(b);
            row.Value = Label(map[b]) + (custom ? "  (custom)" : "");
            row.ValueColor = custom ? Theme.Accent : Theme.Text;
            row.SetContent(Theme.Glyph.Controller, Names[b], custom ? $"Default: {Label(defaults[b])}" : "");
        }
        UpdateSelection();
    }

    void Select(PhysicalButton b)
    {
        _selected = b;
        UpdateSelection();
        _list.ScrollControlIntoView(_rows[b]);
    }

    void UpdateSelection()
    {
        var map = Map();
        foreach (var (b, row) in _rows)
        {
            row.Highlighted = b == _selected;
            row.Invalidate();
        }
        _pad.Highlight = _selected;
        _pad.HighlightXbox = map[_selected];
        _selectedTitle.Text = $"{Names[_selected]}  →  {Label(map[_selected])}";
    }

    void ShowTargets(PhysicalButton b, Control anchor)
    {
        var defaults = ButtonMap.Create(Ctx.Session.Editing.NintendoLayout);
        var current = Ctx.Session.Editing.ButtonOverrides.TryGetValue(b, out var o) ? o : (X360Buttons?)null;
        var menu = new ContextMenuStrip { Renderer = new Theme.MenuRenderer(), BackColor = Theme.Card, ForeColor = Theme.Text };
        var def = new ToolStripMenuItem($"Default ({Label(defaults[b])})") { Checked = current is null };
        def.Click += (_, _) => SetOverride(b, null);
        menu.Items.Add(def);
        menu.Items.Add(new ToolStripSeparator());
        foreach (var (label, value) in Targets)
        {
            var item = new ToolStripMenuItem(label) { Checked = current == value };
            item.Click += (_, _) => SetOverride(b, value);
            menu.Items.Add(item);
        }
        menu.Show(anchor, new Point(Math.Max(0, anchor.Width - 230), anchor.Height));
    }

    void SetOverride(PhysicalButton b, X360Buttons? target)
    {
        Ctx.Session.Update(p =>
        {
            var overrides = new Dictionary<PhysicalButton, X360Buttons>(p.ButtonOverrides);
            if (target is { } t)
                overrides[b] = t;
            else
                overrides.Remove(b);
            return p with { ButtonOverrides = overrides };
        });
    }

    public override void OnLive(in LiveView live)
    {
        var s = live.Snapshot;
        _pad.SetState(s.Input, s.Output, s.Running && s.Device is not null);

        // Pressing a real button selects it.
        uint pressed = s.Input.Buttons & ~_lastButtons;
        _lastButtons = s.Input.Buttons;
        if (pressed != 0 && Visible)
        {
            foreach (var b in Enum.GetValues<PhysicalButton>())
            {
                if ((pressed & PhysicalButtons.Bit(b)) != 0)
                {
                    Select(b);
                    break;
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------------------- Sticks & triggers

/// <summary>Two bars: the raw trigger (white) and what the game gets (accent).</summary>
internal sealed class TriggerMeter : Control
{
    byte _raw, _out;

    public TriggerMeter(string caption)
    {
        Text = caption;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Height = 40;
    }

    public void Set(byte raw, byte output)
    {
        if (raw == _raw && output == _out)
            return;
        (_raw, _out) = (raw, output);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        using var font = new Font("Segoe UI Semibold", 9f);
        TextRenderer.DrawText(g, Text, font, new Rectangle(0, 0, LogicalToDeviceUnits(36), Height), Theme.Text, TextFormatFlags.VerticalCenter);
        float x = LogicalToDeviceUnits(40), w = Width - x - LogicalToDeviceUnits(4);
        void Bar(float y, byte value, Color color)
        {
            var track = new RectangleF(x, y, w, LogicalToDeviceUnits(7));
            using (var path = Theme.RoundedRect(track, track.Height / 2))
            using (var bg = new SolidBrush(Theme.Raised))
                g.FillPath(bg, path);
            if (value > 0)
            {
                using var path = Theme.RoundedRect(track with { Width = Math.Max(track.Height, w * value / 255f) }, track.Height / 2);
                using var fill = new SolidBrush(color);
                g.FillPath(fill, path);
            }
        }
        Bar(LogicalToDeviceUnits(10), _raw, Color.FromArgb(220, Color.White));
        Bar(LogicalToDeviceUnits(23), _out, Theme.Accent);
    }
}

internal sealed class SticksPage : Page
{
    readonly StickPreview _left = new("Left stick") { Width = 210, Height = 230 };
    readonly StickPreview _right = new("Right stick") { Width = 210, Height = 230 };
    readonly FluentSlider _deadzone = new() { Minimum = 0, Maximum = 90, Format = v => $"{v:0}%" };
    readonly FluentSlider _saturation = new() { Minimum = 10, Maximum = 100, Format = v => $"{v:0}%" };
    readonly FluentSlider _threshold = new() { Minimum = 0, Maximum = 90, Format = v => $"{v:0}%" };
    readonly FluentSlider _curve = new() { Minimum = 0.2, Maximum = 4, Step = 0.05, Format = v => v.ToString("0.00") };
    readonly FluentSlider _rumble = new() { Minimum = 0, Maximum = 200, Step = 5, Format = v => $"{v:0}%" };
    readonly TriggerMeter _l2 = new("L2");
    readonly TriggerMeter _r2 = new("R2");
    readonly ToggleSwitch _invLX, _invLY, _invRX, _invRY;
    readonly ActionRow _rLX, _rLY, _rRX, _rRY;
    readonly Label _error = new() { AutoSize = false, Height = 20, ForeColor = Theme.Bad, BackColor = Theme.Card, Font = new Font("Segoe UI", 8.75f) };
    bool _loading;

    public SticksPage(ShellContext ctx) : base(ctx, "Sticks & Triggers", "Deadzones, trigger response and rumble")
    {
        (_rLX, _invLX) = ToggleRow(Theme.Glyph.Sticks, "Invert left stick X", "");
        (_rLY, _invLY) = ToggleRow(Theme.Glyph.Sticks, "Invert left stick Y", "");
        (_rRX, _invRX) = ToggleRow(Theme.Glyph.Sticks, "Invert right stick X", "");
        (_rRY, _invRY) = ToggleRow(Theme.Glyph.Sticks, "Invert right stick Y", "");
        foreach (var r in new[] { _rLX, _rLY, _rRX, _rRY })
            r.Height = 40;

        var grid = Grid(2, 56, 44);
        grid.RowCount = 1;
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var sticks = new RoundCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0), Padding = new Padding(16, 12, 16, 12) };
        var previews = new FlowLayoutPanel { Height = 236, BackColor = Theme.Card, WrapContents = false };
        previews.Controls.Add(_left);
        previews.Controls.Add(_right);
        var legend = Body("White: your input  ·  Blue: game gets  ·  Red: deadzone  ·  Green: full tilt", 20);
        StackTop(sticks, CardTitle("Sticks"), previews, legend, Spacer(4),
            Body("Deadzone: movement ignored around the center (removes drift)", 18), _deadzone,
            Body("Full tilt at: how far you push to reach 100%", 18), _saturation, Spacer(6),
            _rLX, _rLY, _rRX, _rRY, _error);
        grid.Controls.Add(sticks, 0, 0);

        var right = Grid(1, 100);
        right.RowCount = 2;
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 330));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var triggers = new RoundCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(16, 12, 16, 12) };
        StackTop(triggers, CardTitle("Triggers (L2 / R2)"), _l2, _r2, Spacer(6),
            Body("Ignore first: travel ignored at the top (stops a light touch firing)", 18), _threshold,
            Body("Curve: 1.00 = linear, above 1 = finer control near the start", 18), _curve);
        right.Controls.Add(triggers, 0, 0);

        var rumble = new RoundCard { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 12) };
        var test = new PillButton("Test rumble", glyph: Theme.Glyph.Vibrate) { BackColor = Theme.Card };
        var testRow = new FlowLayoutPanel { Height = 40, BackColor = Theme.Card, Padding = new Padding(0, 6, 0, 0) };
        testRow.Controls.Add(test);
        StackTop(rumble, CardTitle("Rumble"), Body("Strength of the game's vibration on the DualSense", 18), _rumble, testRow);
        right.Controls.Add(rumble, 0, 1);
        grid.Controls.Add(right, 1, 0);

        Controls.Add(grid);
        StackTop(this, new EditHeader(ctx.Session), Spacer(8, Color.Transparent));

        void Hook(FluentSlider s) => s.ValueChanged += (_, _) => Apply();
        foreach (var s in new[] { _deadzone, _saturation, _threshold, _curve, _rumble })
            Hook(s);
        foreach (var t in new[] { _invLX, _invLY, _invRX, _invRY })
            t.CheckedChanged += (_, _) => Apply();
        test.Click += (_, _) => Ctx.Actions.TestRumble(_rumble.Value / 100);
        Ctx.Session.Changed += Load;
        Load();
    }

    void Load()
    {
        _loading = true;
        var p = Ctx.Session.Editing;
        _deadzone.SetSilently(p.StickDeadzone * 100);
        _saturation.SetSilently(p.StickOuterSaturation * 100);
        _threshold.SetSilently(p.TriggerThreshold * 100);
        _curve.SetSilently(p.TriggerCurve);
        _rumble.SetSilently(p.RumbleScale * 100);
        _invLX.SetSilently(p.InvertLeftX);
        _invLY.SetSilently(p.InvertLeftY);
        _invRX.SetSilently(p.InvertRightX);
        _invRY.SetSilently(p.InvertRightY);
        _left.SetZones(p.StickDeadzone, p.StickOuterSaturation);
        _right.SetZones(p.StickDeadzone, p.StickOuterSaturation);
        _loading = false;
    }

    void Apply()
    {
        if (_loading)
            return;
        try
        {
            Ctx.Session.Update(p => p with
            {
                StickDeadzone = _deadzone.Value / 100,
                StickOuterSaturation = _saturation.Value / 100,
                TriggerThreshold = _threshold.Value / 100,
                TriggerCurve = _curve.Value,
                RumbleScale = _rumble.Value / 100,
                InvertLeftX = _invLX.Checked,
                InvertLeftY = _invLY.Checked,
                InvertRightX = _invRX.Checked,
                InvertRightY = _invRY.Checked,
            });
            _error.Text = "";
        }
        catch (ArgumentOutOfRangeException)
        {
            _error.Text = "\"Full tilt at\" must be larger than the deadzone.";
        }
    }

    public override void OnLive(in LiveView live)
    {
        // Preview with the profile being edited, so slider changes show before saving.
        var s = live.Snapshot;
        Profile p = Ctx.Session.Editing;
        Mapper mapper;
        try { mapper = new Mapper(p); }
        catch (ArgumentOutOfRangeException) { return; }
        var o = mapper.Map(s.Input);
        _left.SetInput(s.Input.LeftX, s.Input.LeftY, o.ThumbLX, o.ThumbLY);
        _right.SetInput(s.Input.RightX, s.Input.RightY, o.ThumbRX, o.ThumbRY);
        _l2.Set(s.Input.L2, o.LeftTrigger);
        _r2.Set(s.Input.R2, o.RightTrigger);
    }
}
