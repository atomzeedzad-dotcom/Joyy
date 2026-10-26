using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Joyy.Core;

namespace Joyy.App;

/// <summary>
/// Live picture of a controller. Two instances side by side (DualSense in, Xbox 360 out)
/// let the user verify the mapping by eye: press Cross, see A light up.
/// Drawn in a fixed 380x240 design space and scaled to fit.
/// </summary>
internal sealed class PadView : Control
{
    public enum PadKind
    {
        DualSense,
        Xbox,
    }

    const float DesignW = 380, DesignH = 240;

    static readonly Font LabelFont = new("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel);
    static readonly Font SmallFont = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Pixel);
    static readonly Font SymbolFont = new("Segoe UI Symbol", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
    static readonly Font OverlayFont = new("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);

    PadState _ds;
    X360Report _xb;
    bool _active;

    public PadView(PadKind kind)
    {
        Kind = kind;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        MinimumSize = new Size(240, 150);
    }

    public PadKind Kind { get; }

    public void SetState(in PadState physical, in X360Report xbox, bool active)
    {
        if (_active == active && _ds == physical && _xb == xbox)
            return;
        _ds = physical;
        _xb = xbox;
        _active = active;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        float s = Math.Min(Width / DesignW, Height / DesignH);
        g.TranslateTransform((Width - DesignW * s) / 2, (Height - DesignH * s) / 2);
        g.ScaleTransform(s, s);

        DrawBody(g);
        if (Kind == PadKind.DualSense)
            DrawDualSense(g);
        else
            DrawXbox(g);

        if (!_active)
        {
            using var veil = new SolidBrush(Color.FromArgb(185, BackColor));
            g.FillRectangle(veil, 0, 0, DesignW, DesignH);
            DrawCentered(g, "No controller connected", OverlayFont, Theme.Muted, new PointF(DesignW / 2, DesignH / 2));
        }
    }

    void DrawDualSense(Graphics g)
    {
        bool P(PhysicalButton b) => _ds.IsPressed(b);

        Trigger(g, new RectangleF(55, 16, 100, 16), "L2", _ds.L2);
        Trigger(g, new RectangleF(225, 16, 100, 16), "R2", _ds.R2);
        Shoulder(g, new RectangleF(55, 38, 100, 16), "L1", P(PhysicalButton.L1));
        Shoulder(g, new RectangleF(225, 38, 100, 16), "R1", P(PhysicalButton.R1));

        Dpad(g, new PointF(100, 112), P(PhysicalButton.DpadUp), P(PhysicalButton.DpadDown),
            P(PhysicalButton.DpadLeft), P(PhysicalButton.DpadRight));
        Face(g, new PointF(280, 112),
            ("△", Theme.PsTriangle, P(PhysicalButton.Triangle)),
            ("○", Theme.PsCircle, P(PhysicalButton.Circle)),
            ("✕", Theme.PsCross, P(PhysicalButton.Cross)),
            ("□", Theme.PsSquare, P(PhysicalButton.Square)));

        Stick(g, new PointF(150, 172), _ds.LeftX, _ds.LeftY, P(PhysicalButton.L3));
        Stick(g, new PointF(230, 172), _ds.RightX, _ds.RightY, P(PhysicalButton.R3));

        Pill(g, new RectangleF(130, 76, 24, 11), "Create", P(PhysicalButton.Create));
        Touchpad(g, new RectangleF(160, 66, 60, 34), P(PhysicalButton.Touchpad));
        Pill(g, new RectangleF(226, 76, 24, 11), "Options", P(PhysicalButton.Options));
        Round(g, new PointF(190, 122), 9, "PS", P(PhysicalButton.PS));
        Pill(g, new RectangleF(181, 147, 18, 8), "Mute", P(PhysicalButton.Mute));
    }

    void DrawXbox(Graphics g)
    {
        bool P(X360Buttons b) => _xb.IsPressed(b);

        Trigger(g, new RectangleF(55, 16, 100, 16), "LT", _xb.LeftTrigger);
        Trigger(g, new RectangleF(225, 16, 100, 16), "RT", _xb.RightTrigger);
        Shoulder(g, new RectangleF(55, 38, 100, 16), "LB", P(X360Buttons.LeftShoulder));
        Shoulder(g, new RectangleF(225, 38, 100, 16), "RB", P(X360Buttons.RightShoulder));

        // Xbox layout: left stick on top, D-pad below.
        Stick(g, new PointF(100, 112), _xb.ThumbLX, _xb.ThumbLY, P(X360Buttons.LeftThumb));
        Face(g, new PointF(280, 112),
            ("Y", Theme.XbY, P(X360Buttons.Y)),
            ("B", Theme.XbB, P(X360Buttons.B)),
            ("A", Theme.XbA, P(X360Buttons.A)),
            ("X", Theme.XbX, P(X360Buttons.X)));
        Dpad(g, new PointF(150, 172), P(X360Buttons.DpadUp), P(X360Buttons.DpadDown),
            P(X360Buttons.DpadLeft), P(X360Buttons.DpadRight));
        Stick(g, new PointF(230, 172), _xb.ThumbRX, _xb.ThumbRY, P(X360Buttons.RightThumb));

        Pill(g, new RectangleF(144, 96, 22, 11), "Back", P(X360Buttons.Back));
        Round(g, new PointF(190, 88), 11, "", P(X360Buttons.Guide));
        Pill(g, new RectangleF(214, 96, 22, 11), "Start", P(X360Buttons.Start));
    }

    static void DrawBody(Graphics g)
    {
        static GraphicsPath Body(float dx, float dy)
        {
            var path = new GraphicsPath { FillMode = FillMode.Winding };
            using (var top = Theme.RoundedRect(new RectangleF(45 + dx, 62 + dy, 290, 110), 46))
                path.AddPath(top, false);
            path.AddEllipse(38 + dx, 118 + dy, 112, 108);
            path.AddEllipse(230 + dx, 118 + dy, 112, 108);
            return path;
        }

        using var shadow = Body(0, 3);
        using var body = Body(0, 0);
        using var shadowBrush = new SolidBrush(Theme.PadShadow);
        using var bodyBrush = new SolidBrush(Theme.PadBody);
        g.FillPath(shadowBrush, shadow);
        g.FillPath(bodyBrush, body);
    }

    static void Trigger(Graphics g, RectangleF r, string label, byte value)
    {
        using var bg = Theme.RoundedRect(r, 6);
        using var bgBrush = new SolidBrush(Theme.Border);
        g.FillPath(bgBrush, bg);
        if (value > 0)
        {
            var fill = r with { Width = Math.Max(12, r.Width * value / 255f) };
            using var fg = Theme.RoundedRect(fill, 6);
            using var fgBrush = new SolidBrush(Theme.Accent);
            g.FillPath(fgBrush, fg);
        }
        DrawCentered(g, label, LabelFont, value > 120 ? Color.White : Theme.Text, Center(r));
    }

    static void Shoulder(Graphics g, RectangleF r, string label, bool pressed)
    {
        using var path = Theme.RoundedRect(r, 6);
        using var brush = new SolidBrush(pressed ? Theme.Accent : Theme.Card);
        using var pen = new Pen(pressed ? Theme.Accent : Theme.Border, 1.5f);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        DrawCentered(g, label, LabelFont, pressed ? Color.White : Theme.Text, Center(r));
    }

    static void Dpad(Graphics g, PointF c, bool up, bool down, bool left, bool right)
    {
        const float size = 15, offset = 16;
        void Arm(float dx, float dy, bool pressed)
        {
            var r = new RectangleF(c.X + dx - size / 2, c.Y + dy - size / 2, size, size);
            using var path = Theme.RoundedRect(r, 3);
            using var brush = new SolidBrush(pressed ? Theme.Accent : Theme.Card);
            using var pen = new Pen(pressed ? Theme.Accent : Theme.Border, 1.5f);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        Arm(0, -offset, up);
        Arm(0, offset, down);
        Arm(-offset, 0, left);
        Arm(offset, 0, right);
    }

    static void Face(Graphics g, PointF c,
        (string Label, Color Color, bool Pressed) top, (string Label, Color Color, bool Pressed) right,
        (string Label, Color Color, bool Pressed) bottom, (string Label, Color Color, bool Pressed) left)
    {
        const float offset = 22, radius = 12;
        void Button(float dx, float dy, (string Label, Color Color, bool Pressed) b)
        {
            var r = new RectangleF(c.X + dx - radius, c.Y + dy - radius, radius * 2, radius * 2);
            using var brush = new SolidBrush(b.Pressed ? b.Color : Theme.Card);
            using var pen = new Pen(b.Color, 2f);
            g.FillEllipse(brush, r);
            g.DrawEllipse(pen, r);
            var font = b.Label.Length == 1 && char.IsLetter(b.Label[0]) ? LabelFont : SymbolFont;
            DrawCentered(g, b.Label, font, b.Pressed ? Color.White : b.Color, Center(r));
        }
        Button(0, -offset, top);
        Button(offset, 0, right);
        Button(0, offset, bottom);
        Button(-offset, 0, left);
    }

    static void Stick(Graphics g, PointF c, short x, short y, bool pressed)
    {
        const float outer = 25, knob = 11, travel = 14;
        using var ring = new SolidBrush(Theme.Card);
        using var ringPen = new Pen(pressed ? Theme.Accent : Theme.Border, pressed ? 3f : 1.5f);
        g.FillEllipse(ring, c.X - outer, c.Y - outer, outer * 2, outer * 2);
        g.DrawEllipse(ringPen, c.X - outer, c.Y - outer, outer * 2, outer * 2);

        // Y is "up = positive", screen Y grows downward.
        float kx = c.X + x / 32768f * travel;
        float ky = c.Y - y / 32768f * travel;
        using var knobBrush = new SolidBrush(pressed ? Theme.Accent : Theme.Knob);
        g.FillEllipse(knobBrush, kx - knob, ky - knob, knob * 2, knob * 2);
    }

    static void Pill(Graphics g, RectangleF r, string caption, bool pressed)
    {
        using var path = Theme.RoundedRect(r, r.Height / 2);
        using var brush = new SolidBrush(pressed ? Theme.Accent : Theme.Card);
        using var pen = new Pen(pressed ? Theme.Accent : Theme.Border, 1.5f);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        if (caption.Length > 0)
            DrawCentered(g, caption, SmallFont, pressed ? Theme.Accent : Theme.Muted, new PointF(r.X + r.Width / 2, r.Y - 7));
    }

    static void Touchpad(Graphics g, RectangleF r, bool pressed)
    {
        using var path = Theme.RoundedRect(r, 6);
        using var brush = new SolidBrush(pressed ? Theme.Accent : Theme.Card);
        using var pen = new Pen(pressed ? Theme.Accent : Theme.Border, 1.5f);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        DrawCentered(g, "Touchpad", SmallFont, pressed ? Color.White : Theme.Muted, Center(r));
    }

    static void Round(Graphics g, PointF c, float radius, string label, bool pressed)
    {
        var r = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
        using var brush = new SolidBrush(pressed ? Theme.Accent : Theme.Card);
        using var pen = new Pen(pressed ? Theme.Accent : Theme.Border, 1.5f);
        g.FillEllipse(brush, r);
        g.DrawEllipse(pen, r);
        if (label.Length > 0)
            DrawCentered(g, label, SmallFont, pressed ? Color.White : Theme.Muted, c);
        else
        {
            using var mark = new Pen(pressed ? Color.White : Theme.XbA, 2f);
            g.DrawEllipse(mark, c.X - radius * 0.5f, c.Y - radius * 0.5f, radius, radius);
        }
    }

    static PointF Center(RectangleF r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    static void DrawCentered(Graphics g, string text, Font font, Color color, PointF center)
    {
        var size = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, center.X - size.Width / 2, center.Y - size.Height / 2, StringFormat.GenericTypographic);
    }
}
