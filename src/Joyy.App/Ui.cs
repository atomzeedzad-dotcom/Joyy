using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Joyy.App;

// Fluent-style building blocks for the DSX-like shell. Cards are rounded and sit on the window's
// gradient; everything inside a card is opaque (Theme.Card) so text renders crisply.

/// <summary>Rounded card. Transparent corners show the window gradient behind it.</summary>
internal class RoundCard : Panel
{
    public RoundCard()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Padding = new Padding(16);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Theme.Card;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Outline { get; set; } = Theme.Border;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 10;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(r, LogicalToDeviceUnits(Radius));
        using var fill = new SolidBrush(Fill);
        using var pen = new Pen(Outline);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }
}

/// <summary>Transparent layout host that lets the window gradient show through.</summary>
internal sealed class ClearPanel : Panel
{
    public ClearPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }
}

/// <summary>
/// Base for owner-drawn clickable things. It is a Button underneath so it keeps keyboard focus,
/// Enter/Space activation and UI Automation "Invoke".
/// </summary>
internal abstract class OwnerButton : Button
{
    protected bool Hovered { get; private set; }
    protected bool Pressed { get; private set; }

    protected OwnerButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { Hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hovered = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (BackColor.A < 255)
            ButtonRenderer.DrawParentBackground(g, ClientRectangle, this);
        else
            g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Draw(g);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Color.FromArgb(120, Theme.Accent)) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(focus, 1, 1, Width - 3, Height - 3);
        }
    }

    protected abstract void Draw(Graphics g);

    protected int Px(int logical) => LogicalToDeviceUnits(logical);

    protected void DrawGlyph(Graphics g, string glyph, Rectangle bounds, Color color, float points)
    {
        using var font = Theme.IconFont(points);
        TextRenderer.DrawText(g, glyph, font, bounds, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected void FillRounded(Graphics g, RectangleF r, float radius, Color color)
    {
        using var path = Theme.RoundedRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }
}

/// <summary>Home-page shortcut tile: icon, title, one-line description, chevron. A card itself.</summary>
internal sealed class Tile : OwnerButton
{
    readonly string _glyph;
    string _subtitle;
    Color _subtitleColor = Theme.Muted;

    public Tile(string glyph, string title, string subtitle)
    {
        _glyph = glyph;
        _subtitle = subtitle;
        Text = title;
        AccessibleName = title;
        BackColor = Color.Transparent;
        Font = new Font("Segoe UI Semibold", 9.75f);
        Height = 62;
        Margin = new Padding(0, 0, 10, 10);
    }

    /// <summary>Live status line under the title (e.g. "On · games see 1 pad").</summary>
    public void SetSubtitle(string text, Color? color = null)
    {
        var c = color ?? Theme.Muted;
        if (text == _subtitle && c == _subtitleColor)
            return;
        _subtitle = text;
        _subtitleColor = c;
        Invalidate();
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundedRect(r, Px(10)))
        using (var fill = new SolidBrush(Hovered ? Theme.Hover : Theme.Card))
        using (var pen = new Pen(Theme.Border))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        DrawGlyph(g, _glyph, new Rectangle(Px(12), 0, Px(30), Height), Theme.Text, 13f);
        using var small = new Font("Segoe UI", 8.25f);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Px(50), Px(12), Width - Px(80), Px(20)), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _subtitle, small, new Rectangle(Px(50), Px(32), Width - Px(80), Px(18)), _subtitleColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        DrawGlyph(g, Theme.Glyph.Chevron, new Rectangle(Width - Px(32), 0, Px(20), Height), Theme.Muted, 8f);
    }
}

/// <summary>
/// List row inside a card: icon, title, description, and on the right either a chevron, a value
/// text, or nothing (when a <see cref="Trailing"/> control is placed over it by the caller).
/// </summary>
internal sealed class ActionRow : OwnerButton
{
    string _glyph;
    string _subtitle;

    public ActionRow(string glyph, string title, string subtitle, bool chevron = true)
    {
        _glyph = glyph;
        _subtitle = subtitle;
        Text = title;
        AccessibleName = title;
        Chevron = chevron;
        Font = new Font("Segoe UI Semibold", 9.5f);
        Height = 54;
        Dock = DockStyle.Top;
    }

    public bool Chevron { get; set; }

    /// <summary>Text drawn on the right (e.g. current value), before the chevron.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Value { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ValueColor { get; set; } = Theme.Muted;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GlyphColor { get; set; } = Theme.Text;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Highlighted { get; set; }

    public void SetContent(string glyph, string title, string subtitle)
    {
        _glyph = glyph;
        _subtitle = subtitle;
        Text = title;
        Invalidate();
    }

    /// <summary>Right-hand inset to leave free for a control the caller docks over the row.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int TrailingWidth { get; set; }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(Px(2), Px(2), Width - Px(4), Height - Px(4));
        if (Highlighted || (Hovered && Enabled))
            FillRounded(g, r, Px(8), Highlighted ? Color.FromArgb(0x14, 0x22, 0x3A) : Theme.Hover);
        if (Highlighted)
        {
            using var path = Theme.RoundedRect(r, Px(8));
            using var pen = new Pen(Theme.Accent, 1.5f);
            g.DrawPath(pen, path);
        }

        var text = Enabled ? Theme.Text : Theme.Muted;
        DrawGlyph(g, _glyph, new Rectangle(Px(10), 0, Px(28), Height), Enabled ? GlyphColor : Theme.Muted, 12f);
        int right = Width - Px(12) - TrailingWidth - (Chevron ? Px(24) : 0);
        bool twoLines = _subtitle.Length > 0;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Px(46), twoLines ? Px(9) : 0, right - Px(46), twoLines ? Px(20) : Height),
            text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | (twoLines ? 0 : TextFormatFlags.VerticalCenter));
        if (twoLines)
        {
            using var small = new Font("Segoe UI", 8.25f);
            TextRenderer.DrawText(g, _subtitle, small, new Rectangle(Px(46), Px(29), right - Px(46), Px(18)), Theme.Muted, TextFormatFlags.EndEllipsis);
        }
        if (Value is { Length: > 0 })
        {
            using var valueFont = new Font("Segoe UI", 9f);
            TextRenderer.DrawText(g, Value, valueFont, new Rectangle(Px(46), 0, right - Px(46), Height), ValueColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        if (Chevron)
            DrawGlyph(g, Theme.Glyph.Chevron, new Rectangle(Width - Px(32), 0, Px(20), Height), Theme.Muted, 8f);
    }
}

/// <summary>Pill on/off switch (accent when on).</summary>
internal sealed class ToggleSwitch : OwnerButton
{
    bool _checked;

    public ToggleSwitch()
    {
        Size = new Size(44, 22);
        AccessibleRole = AccessibleRole.CheckButton;
    }

    public event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Sets the state without raising <see cref="CheckedChanged"/>.</summary>
    public void SetSilently(bool value)
    {
        _checked = value;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void Draw(Graphics g)
    {
        float h = Height - Px(4), w = Width - Px(4);
        var track = new RectangleF(Px(2), Px(2), w, h);
        var on = Enabled ? Theme.Accent : Theme.Idle;
        if (_checked)
        {
            FillRounded(g, track, h / 2, on);
        }
        else
        {
            using var path = Theme.RoundedRect(track, h / 2);
            using var pen = new Pen(Theme.Muted, 1.3f);
            g.DrawPath(pen, path);
        }
        float knob = h - Px(8);
        float x = _checked ? track.Right - knob - Px(4) : track.X + Px(4);
        using var brush = new SolidBrush(_checked ? Color.White : Theme.Muted);
        g.FillEllipse(brush, x, track.Y + Px(4), knob, knob);
    }
}

/// <summary>Rounded button: accent ("primary") or subtle; optional icon.</summary>
internal sealed class PillButton : OwnerButton
{
    readonly bool _primary;
    readonly string? _glyph;

    public PillButton(string text, bool primary = false, string? glyph = null)
    {
        _primary = primary;
        _glyph = glyph;
        Text = text;
        AccessibleName = text;
        Font = new Font("Segoe UI Semibold", 9f);
        var size = TextRenderer.MeasureText(text, Font);
        Size = new Size(size.Width + (glyph is null ? 32 : 52), 32);
        Margin = new Padding(0, 0, 8, 0);
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fill = !Enabled ? Theme.Raised
            : _primary ? (Pressed ? ControlPaint.Dark(Theme.Accent, 0.05f) : Hovered ? ControlPaint.Light(Theme.Accent, 0.12f) : Theme.Accent)
            : (Pressed ? Theme.Border : Hovered ? Theme.Hover : Theme.Raised);
        FillRounded(g, r, Px(6), fill);
        if (!_primary)
        {
            using var path = Theme.RoundedRect(r, Px(6));
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        var color = !Enabled ? Theme.Muted : _primary ? Color.White : Theme.Text;
        int x = Px(14);
        if (_glyph is not null)
        {
            DrawGlyph(g, _glyph, new Rectangle(x - Px(2), 0, Px(20), Height), color, 9f);
            x += Px(22);
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - Px(10), Height), color, TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Two-or-more option selector (like DSX's Virtual Device switcher).</summary>
internal sealed class Segmented : Control
{
    readonly string[] _options;
    int _selected;
    int _hover = -1;

    public Segmented(params string[] options)
    {
        _options = options;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Height = 36;
        Font = new Font("Segoe UI Semibold", 9f);
        Cursor = Cursors.Hand;
    }

    public event Action<int>? SelectionChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            Invalidate();
        }
    }

    Rectangle Segment(int i)
    {
        int w = Width / _options.Length;
        return new Rectangle(i * w, 0, i == _options.Length - 1 ? Width - i * w : w, Height);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = Math.Clamp(e.X * _options.Length / Math.Max(1, Width), 0, _options.Length - 1);
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = Math.Clamp(e.X * _options.Length / Math.Max(1, Width), 0, _options.Length - 1);
        if (i != _selected)
        {
            _selected = i;
            Invalidate();
            SelectionChanged?.Invoke(i);
        }
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), LogicalToDeviceUnits(8)))
        using (var fill = new SolidBrush(Theme.Raised))
            g.FillPath(fill, path);

        for (int i = 0; i < _options.Length; i++)
        {
            var seg = Segment(i);
            var inner = RectangleF.Inflate(seg, -LogicalToDeviceUnits(3), -LogicalToDeviceUnits(3));
            if (i == _selected || i == _hover)
            {
                using var path = Theme.RoundedRect(inner, LogicalToDeviceUnits(6));
                using var fill = new SolidBrush(i == _selected ? Theme.Accent : Theme.Hover);
                g.FillPath(fill, path);
            }
            TextRenderer.DrawText(g, _options[i], Font, seg, i == _selected ? Color.White : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>Horizontal slider with an accent fill and the value shown on the right.</summary>
internal sealed class FluentSlider : Control
{
    double _value;
    bool _dragging;

    public FluentSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Card;
        Height = 30;
        Font = new Font("Segoe UI Semibold", 9f);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public double Step { get; set; } = 1;

    /// <summary>Formats the value label, e.g. v => $"{v:0}%".</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<double, string> Format { get; set; } = v => v.ToString("0");

    public event EventHandler? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => _value;
        set
        {
            double v = Math.Clamp(Math.Round(value / Step) * Step, Minimum, Maximum);
            if (Math.Abs(v - _value) < 1e-9)
                return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetSilently(double value)
    {
        _value = Math.Clamp(value, Minimum, Maximum);
        Invalidate();
    }

    int LabelWidth => LogicalToDeviceUnits(56);
    RectangleF Track => new(LogicalToDeviceUnits(8), Height / 2f - LogicalToDeviceUnits(2), Width - LabelWidth - LogicalToDeviceUnits(16), LogicalToDeviceUnits(4));

    void SetFromX(int x)
    {
        var t = Track;
        Value = Minimum + Math.Clamp((x - t.X) / t.Width, 0, 1) * (Maximum - Minimum);
    }

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); _dragging = true; SetFromX(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Left or Keys.Down) Value -= Step;
        if (e.KeyCode is Keys.Right or Keys.Up) Value += Step;
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var t = Track;
        float frac = Maximum > Minimum ? (float)((_value - Minimum) / (Maximum - Minimum)) : 0;
        using (var path = Theme.RoundedRect(t, t.Height / 2))
        using (var bg = new SolidBrush(Theme.Raised))
            g.FillPath(bg, path);
        if (frac > 0)
        {
            using var path = Theme.RoundedRect(new RectangleF(t.X, t.Y, Math.Max(t.Height, t.Width * frac), t.Height), t.Height / 2);
            using var fill = new SolidBrush(Enabled ? Theme.Accent : Theme.Idle);
            g.FillPath(fill, path);
        }
        float k = LogicalToDeviceUnits(Focused ? 9 : 8);
        float cx = t.X + t.Width * frac;
        using (var ring = new SolidBrush(Theme.Card))
            g.FillEllipse(ring, cx - k, Height / 2f - k, k * 2, k * 2);
        using (var knob = new SolidBrush(Enabled ? Theme.Accent : Theme.Idle))
            g.FillEllipse(knob, cx - k + 3, Height / 2f - k + 3, k * 2 - 6, k * 2 - 6);
        TextRenderer.DrawText(g, Format(_value), Font, new Rectangle(Width - LabelWidth, 0, LabelWidth, Height), Theme.Text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Icon + label + value block used in "Device information" grids.</summary>
internal sealed class InfoTile : Control
{
    readonly string _glyph;
    string _value = "—";

    public InfoTile(string glyph, string label)
    {
        _glyph = glyph;
        Text = label;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Height = 50;
        Dock = DockStyle.Fill;
        Margin = new Padding(4);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ValueColor { get; set; } = Theme.Muted;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), LogicalToDeviceUnits(6)))
        using (var fill = new SolidBrush(Theme.Raised))
            g.FillPath(fill, path);
        using (var icon = Theme.IconFont(11f))
            TextRenderer.DrawText(g, _glyph, icon, new Rectangle(LogicalToDeviceUnits(8), 0, LogicalToDeviceUnits(26), Height), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using var title = new Font("Segoe UI Semibold", 8.75f);
        using var value = new Font("Segoe UI", 8.25f);
        int x = LogicalToDeviceUnits(40);
        TextRenderer.DrawText(g, Text, title, new Rectangle(x, LogicalToDeviceUnits(8), Width - x - 6, LogicalToDeviceUnits(18)), Theme.Text, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, _value, value, new Rectangle(x, LogicalToDeviceUnits(26), Width - x - 6, LogicalToDeviceUnits(18)), ValueColor, TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A status line with a check / warning / cross glyph.</summary>
internal sealed class CheckLine : Control
{
    public enum State
    {
        Ok,
        Warn,
        Bad,
        Off,
    }

    State _state;
    string _detail = "";

    public CheckLine(string title)
    {
        Text = title;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Height = 44;
        Dock = DockStyle.Top;
    }

    public void Set(State state, string detail)
    {
        if (_state == state && _detail == detail)
            return;
        _state = state;
        _detail = detail;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var (glyph, color) = _state switch
        {
            State.Ok => (Theme.Glyph.Check, Theme.Good),
            State.Warn => (Theme.Glyph.Warning, Theme.Warn),
            State.Bad => (Theme.Glyph.Cancel, Theme.Bad),
            _ => (Theme.Glyph.Cancel, Theme.Idle),
        };
        using (var icon = Theme.IconFont(10f))
            TextRenderer.DrawText(g, glyph, icon, new Rectangle(LogicalToDeviceUnits(10), 0, LogicalToDeviceUnits(22), Height), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using var title = new Font("Segoe UI Semibold", 9f);
        using var detail = new Font("Segoe UI", 8.25f);
        int x = LogicalToDeviceUnits(42);
        TextRenderer.DrawText(g, Text, title, new Rectangle(x, LogicalToDeviceUnits(5), Width - x, LogicalToDeviceUnits(18)), Theme.Text, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, _detail, detail, new Rectangle(x, LogicalToDeviceUnits(23), Width - x, LogicalToDeviceUnits(18)), Theme.Muted, TextFormatFlags.EndEllipsis);
    }
}

internal static class UiFactory
{
    public static Label Title(string text, float size = 15f) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent,
        Font = new Font("Segoe UI Semibold", size), Margin = new Padding(0, 0, 0, 2),
    };

    public static Label SectionLabel(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Card,
        Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(0, 0, 0, 6),
    };

    public static Label Muted(string text, Color? back = null) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.Muted, BackColor = back ?? Theme.Card,
        Font = new Font("Segoe UI", 8.75f),
    };
}
