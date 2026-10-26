using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Joyy.Core;

namespace Joyy.App;

/// <summary>
/// Live, life-like drawing of a DualSense or an Xbox 360 controller. Pressed buttons glow on
/// the controller itself, triggers fill with how far they are pulled, and the stick caps move.
/// Drawn as vectors in a fixed 400x270 design space and scaled to fit.
/// </summary>
internal sealed class PadView : Control
{
    public enum PadKind
    {
        DualSense,
        Xbox,
    }

    const float W = 400, H = 270;

    static readonly Font TinyFont = new("Segoe UI Semibold", 8f, GraphicsUnit.Pixel);
    static readonly Font LabelFont = new("Segoe UI Semibold", 10f, GraphicsUnit.Pixel);
    static readonly Font FaceFont = new("Segoe UI", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel);
    static readonly Font OverlayFont = new("Segoe UI Semibold", 15f, GraphicsUnit.Pixel);

    // Left half of each outline, top-center to bottom-center, as cubic Bezier points; mirrored for the right.
    static readonly PointF[] DualSenseOutline =
    {
        new(200, 60),
        new(170, 60), new(142, 57), new(120, 55),
        new(98, 53), new(78, 50), new(64, 60),
        new(48, 72), new(42, 102), new(43, 130),
        new(44, 172), new(48, 214), new(66, 238),
        new(80, 256), new(110, 256), new(122, 238),
        new(134, 220), new(140, 198), new(154, 187),
        new(166, 178), new(184, 176), new(200, 176),
    };

    static readonly PointF[] Xbox360Outline =
    {
        new(200, 64),
        new(176, 64), new(150, 55), new(122, 55),
        new(92, 55), new(66, 58), new(52, 76),
        new(36, 98), new(34, 132), new(40, 162),
        new(48, 202), new(62, 236), new(84, 246),
        new(104, 256), new(126, 246), new(132, 226),
        new(138, 206), new(150, 188), new(170, 182),
        new(182, 178), new(192, 178), new(200, 178),
    };

    PadState _ds;
    X360Report _xb;
    bool _active;
    PhotoSkin? _skin;

    /// <summary>A product photo to draw instead of the built-in artwork; null for the artwork.</summary>
    public PhotoSkin? Skin
    {
        get => _skin;
        set
        {
            _skin = value;
            Invalidate();
        }
    }

    public PadView(PadKind kind)
    {
        Kind = kind;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        MinimumSize = new Size(240, 160);
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
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(BackColor);

        if (_skin is not null)
        {
            DrawPhoto(g, _skin);
        }
        else
        {
            var state = g.Save();
            float s = Math.Min(Width / W, Height / H);
            g.TranslateTransform((Width - W * s) / 2, (Height - H * s) / 2);
            g.ScaleTransform(s, s);
            if (Kind == PadKind.DualSense)
                DrawDualSense(g);
            else
                DrawXbox360(g);
            g.Restore(state);
        }

        if (!_active)
        {
            using var veil = new SolidBrush(Color.FromArgb(200, BackColor));
            g.FillRectangle(veil, ClientRectangle);
            DrawCentered(g, "No controller connected", OverlayFont, Theme.Muted, new PointF(Width / 2f, Height / 2f));
        }
    }

    // ------------------------------------------------------------------ photo mode

    // Button positions measured on the reference product photos, both 1600x1600:
    //   DualSense: PlayStation Direct "dualsense-ps5-controller-white-accessory-front"
    //   Xbox:      Microsoft Store "gldn-Xbox-Cntrl-Black-b00" (Xbox Wireless Controller, Carbon Black)
    // Scaled to whatever resolution the installed photo has.
    const float DsRefW = 1600, DsRefH = 1600, XbRefW = 1600, XbRefH = 1600;

    void DrawPhoto(Graphics g, PhotoSkin skin)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        float band = Height * 0.15f; // room above the photo for the trigger bars
        var area = new RectangleF(6, band, Width - 12, Height - band - 4);
        float scale = Math.Min(area.Width / skin.Image.Width, area.Height / skin.Image.Height);
        var dest = new RectangleF(
            area.X + (area.Width - skin.Image.Width * scale) / 2,
            area.Y + (area.Height - skin.Image.Height * scale) / 2,
            skin.Image.Width * scale, skin.Image.Height * scale);

        // Soft light behind the controller so a black one still stands out on the dark theme.
        using (var halo = new GraphicsPath())
        {
            var light = RectangleF.Inflate(dest, dest.Width * 0.12f, dest.Height * 0.18f);
            halo.AddEllipse(light);
            using var brush = new PathGradientBrush(halo)
            {
                CenterColor = Color.FromArgb(46, 0xA8, 0xB8, 0xD8),
                SurroundColors = new[] { Color.FromArgb(0, 0xA8, 0xB8, 0xD8) },
            };
            g.FillPath(brush, halo);
        }
        g.DrawImage(skin.Image, dest);

        bool ds = Kind == PadKind.DualSense;
        float refW = ds ? DsRefW : XbRefW, refH = ds ? DsRefH : XbRefH;
        PointF P(float x, float y) => new(
            dest.X + (x / refW * skin.Source.Width - skin.Crop.X) * scale,
            dest.Y + (y / refH * skin.Source.Height - skin.Crop.Y) * scale);
        float R(float r) => r / refW * skin.Source.Width * scale;
        RectangleF Bar(float centerX) => new(P(centerX, 0).X - R(ds ? 80 : 110), band * 0.28f, R(ds ? 160 : 220), Math.Max(10, band * 0.42f));

        if (ds)
        {
            bool B(PhysicalButton b) => _ds.IsPressed(b);
            var accent = Theme.Accent;
            TriggerBar(g, Bar(550), "L2", _ds.L2, accent);
            TriggerBar(g, Bar(1060), "R2", _ds.R2, accent);
            if (B(PhysicalButton.L1)) GlowEllipse(g, P(550, 556), R(62), R(16), accent);
            if (B(PhysicalButton.R1)) GlowEllipse(g, P(1060, 556), R(62), R(16), accent);

            PhotoButton(g, P(1058, 639), R(27), Theme.PsTriangle, B(PhysicalButton.Triangle));
            PhotoButton(g, P(1117, 699), R(27), Theme.PsCircle, B(PhysicalButton.Circle));
            PhotoButton(g, P(1058, 758), R(27), Theme.PsCross, B(PhysicalButton.Cross));
            PhotoButton(g, P(998, 699), R(27), Theme.PsSquare, B(PhysicalButton.Square));
            PhotoButton(g, P(551, 656), R(25), accent, B(PhysicalButton.DpadUp));
            PhotoButton(g, P(551, 745), R(25), accent, B(PhysicalButton.DpadDown));
            PhotoButton(g, P(505, 699), R(25), accent, B(PhysicalButton.DpadLeft));
            PhotoButton(g, P(597, 699), R(25), accent, B(PhysicalButton.DpadRight));
            PhotoButton(g, P(614, 605), R(15), accent, B(PhysicalButton.Create));
            PhotoButton(g, P(994, 605), R(15), accent, B(PhysicalButton.Options));
            PhotoButton(g, P(805, 805), R(30), accent, B(PhysicalButton.PS));
            PhotoButton(g, P(805, 853), R(18), Color.FromArgb(0xFF, 0x8A, 0x3D), B(PhysicalButton.Mute));

            if (B(PhysicalButton.Touchpad))
            {
                var tl = P(643, 560);
                var br = P(963, 724);
                var pad = RectangleF.FromLTRB(tl.X, tl.Y, br.X, br.Y);
                GlowRect(g, pad, R(20), accent);
                using var path = Theme.RoundedRect(pad, R(26));
                using var fill = new SolidBrush(Color.FromArgb(110, accent));
                g.FillPath(fill, path);
            }

            PhotoStick(g, P(675, 809), R(52), _ds.LeftX, _ds.LeftY, B(PhysicalButton.L3), accent);
            PhotoStick(g, P(937, 809), R(52), _ds.RightX, _ds.RightY, B(PhysicalButton.R3), accent);
        }
        else
        {
            bool B(X360Buttons b) => _xb.IsPressed(b);
            var accent = Theme.XbGreen;
            TriggerBar(g, Bar(493), "LT", _xb.LeftTrigger, accent);
            TriggerBar(g, Bar(1122), "RT", _xb.RightTrigger, accent);
            if (B(X360Buttons.LeftShoulder)) GlowEllipse(g, P(493, 384), R(115), R(24), accent);
            if (B(X360Buttons.RightShoulder)) GlowEllipse(g, P(1122, 384), R(115), R(24), accent);

            PhotoButton(g, P(1120, 524), R(44), Theme.XbY, B(X360Buttons.Y));
            PhotoButton(g, P(1205, 607), R(44), Theme.XbB, B(X360Buttons.B));
            PhotoButton(g, P(1120, 692), R(44), Theme.XbA, B(X360Buttons.A));
            PhotoButton(g, P(1034, 607), R(44), Theme.XbX, B(X360Buttons.X));
            PhotoButton(g, P(801, 479), R(48), accent, B(X360Buttons.Guide));
            PhotoButton(g, P(709, 606), R(28), accent, B(X360Buttons.Back));   // View button
            PhotoButton(g, P(891, 606), R(28), accent, B(X360Buttons.Start));  // Menu button
            PhotoButton(g, P(636, 743), R(30), accent, B(X360Buttons.DpadUp));
            PhotoButton(g, P(636, 866), R(30), accent, B(X360Buttons.DpadDown));
            PhotoButton(g, P(575, 803), R(30), accent, B(X360Buttons.DpadLeft));
            PhotoButton(g, P(698, 803), R(30), accent, B(X360Buttons.DpadRight));

            PhotoStick(g, P(476, 610), R(72), _xb.ThumbLX, _xb.ThumbLY, B(X360Buttons.LeftThumb), accent);
            PhotoStick(g, P(960, 782), R(72), _xb.ThumbRX, _xb.ThumbRY, B(X360Buttons.RightThumb), accent);
        }
    }

    /// <summary>Lights a button on the photo: colored glow, tinted cap and a bright ring.</summary>
    static void PhotoButton(Graphics g, PointF c, float r, Color color, bool pressed)
    {
        if (!pressed)
            return;
        Glow(g, c, r * 2.3f, color, 210);
        using var fill = new SolidBrush(Color.FromArgb(140, color));
        using var ring = new Pen(Color.FromArgb(230, ControlPaint.Light(color, 0.6f)), Math.Max(1.5f, r * 0.13f));
        g.FillEllipse(fill, c.X - r, c.Y - r, r * 2, r * 2);
        g.DrawEllipse(ring, c.X - r, c.Y - r, r * 2, r * 2);
    }

    /// <summary>Photo sticks cannot move, so a dot shows where the stick points (and a ring when clicked).</summary>
    static void PhotoStick(Graphics g, PointF c, float r, short x, short y, bool pressed, Color accent)
    {
        if (pressed)
        {
            Glow(g, c, r * 1.7f, accent, 200);
            using var ring = new Pen(Color.FromArgb(230, ControlPaint.Light(accent, 0.5f)), Math.Max(2f, r * 0.09f));
            g.DrawEllipse(ring, c.X - r, c.Y - r, r * 2, r * 2);
        }

        float dx = x / 32768f, dy = -y / 32768f;
        bool deflected = dx * dx + dy * dy > 0.0025f;
        var dot = new PointF(c.X + dx * r * 0.62f, c.Y + dy * r * 0.62f);
        if (deflected)
        {
            using var line = new Pen(Color.FromArgb(150, accent), Math.Max(1.5f, r * 0.06f)) { EndCap = LineCap.Round, StartCap = LineCap.Round };
            g.DrawLine(line, c, dot);
        }
        float dr = Math.Max(3f, r * 0.15f);
        using var brush = new SolidBrush(Color.FromArgb(deflected ? 240 : 110, accent));
        using var rim = new Pen(Color.FromArgb(deflected ? 230 : 90, Color.White), 1.2f);
        g.FillEllipse(brush, dot.X - dr, dot.Y - dr, dr * 2, dr * 2);
        g.DrawEllipse(rim, dot.X - dr, dot.Y - dr, dr * 2, dr * 2);
    }

    static void TriggerBar(Graphics g, RectangleF r, string label, byte value, Color accent)
    {
        using var path = Theme.RoundedRect(r, r.Height / 2);
        using (var bg = new SolidBrush(Theme.Raised))
            g.FillPath(bg, path);
        if (value > 0)
        {
            var state = g.Save();
            g.SetClip(path);
            using var fill = new SolidBrush(accent);
            g.FillRectangle(fill, r.X, r.Y, r.Width * value / 255f, r.Height);
            g.Restore(state);
            if (value > 200)
                GlowRect(g, r, r.Height * 0.6f, accent, 110);
        }
        using (var rim = new Pen(Theme.Border))
            g.DrawPath(rim, path);
        DrawCentered(g, label, LabelFont, value > 120 ? Color.White : Theme.Muted, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2));
    }

    static void GlowEllipse(Graphics g, PointF c, float rx, float ry, Color color, int alpha = 200)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(c.X - rx * 1.5f, c.Y - ry * 1.8f, rx * 3f, ry * 3.6f);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(alpha, color),
            SurroundColors = new[] { Color.FromArgb(0, color) },
            FocusScales = new PointF(0.45f, 0.35f),
        };
        g.FillPath(brush, path);
    }

    // ------------------------------------------------------------------ DualSense

    void DrawDualSense(Graphics g)
    {
        bool P(PhysicalButton b) => _ds.IsPressed(b);
        var accent = Theme.Accent;
        var dark = Color.FromArgb(0x1C, 0x1F, 0x25);

        Trigger(g, new RectangleF(76, 26, 60, 30), "L2", _ds.L2, dark, accent);
        Trigger(g, new RectangleF(264, 26, 60, 30), "R2", _ds.R2, dark, accent);
        Bumper(g, new RectangleF(72, 42, 66, 16), "L1", P(PhysicalButton.L1), Color.FromArgb(0xE9, 0xEB, 0xEF), accent);
        Bumper(g, new RectangleF(262, 42, 66, 16), "R1", P(PhysicalButton.R1), Color.FromArgb(0xE9, 0xEB, 0xEF), accent);

        using var body = Mirrored(DualSenseOutline);
        PaintBody(g, body, Color.FromArgb(0xF7, 0xF8, 0xFA), Color.FromArgb(0xD3, 0xD6, 0xDC));

        // Black center section, clipped to the shell.
        var clip = g.Save();
        g.SetClip(body);
        using (var core = new GraphicsPath())
        {
            core.AddClosedCurve(new PointF[] { new(132, 56), new(268, 56), new(286, 122), new(262, 236), new(138, 236), new(114, 122) }, 0.35f);
            // Gradient covers the whole core; a shorter one would tile and leave a light band.
            using var coreFill = new LinearGradientBrush(new RectangleF(110, 50, 180, 200),
                Color.FromArgb(0x26, 0x29, 0x31), Color.FromArgb(0x0B, 0x0C, 0x0F), LinearGradientMode.Vertical);
            g.FillPath(coreFill, core);
        }
        g.Restore(clip);

        // Touchpad with the light bar along both sides.
        var pad = new RectangleF(146, 58, 108, 50);
        if (P(PhysicalButton.Touchpad))
            GlowRect(g, pad, 10, accent);
        using (var tp = Theme.RoundedRect(pad, 9))
        {
            using var tpFill = new LinearGradientBrush(pad,
                P(PhysicalButton.Touchpad) ? ControlPaint.Light(accent, 0.2f) : Color.FromArgb(0x2B, 0x2F, 0x37),
                P(PhysicalButton.Touchpad) ? accent : Color.FromArgb(0x17, 0x19, 0x1E), LinearGradientMode.Vertical);
            using var tpPen = new Pen(Color.FromArgb(0x3A, 0x3F, 0x48), 1f);
            g.FillPath(tpFill, tp);
            g.DrawPath(tpPen, tp);
        }
        foreach (float x in new[] { 140.5f, 256.5f })
        {
            var bar = new RectangleF(x, 64, 3, 38);
            GlowRect(g, bar, 4, Color.FromArgb(0x3B, 0x82, 0xF6), 140);
            using var barPath = Theme.RoundedRect(bar, 1.5f);
            using var barBrush = new SolidBrush(Color.FromArgb(0x7F, 0xB0, 0xFF));
            g.FillPath(barBrush, barPath);
        }

        SmallKey(g, new RectangleF(125, 62, 8, 17), P(PhysicalButton.Create), accent);
        SmallKey(g, new RectangleF(267, 62, 8, 17), P(PhysicalButton.Options), accent);

        PsDpad(g, new PointF(86, 112), P(PhysicalButton.DpadUp), P(PhysicalButton.DpadDown),
            P(PhysicalButton.DpadLeft), P(PhysicalButton.DpadRight), accent);

        var face = new PointF(314, 112);
        PsFace(g, new PointF(face.X, face.Y - 22), PsSymbol.Triangle, Theme.PsTriangle, P(PhysicalButton.Triangle));
        PsFace(g, new PointF(face.X + 22, face.Y), PsSymbol.Circle, Theme.PsCircle, P(PhysicalButton.Circle));
        PsFace(g, new PointF(face.X, face.Y + 22), PsSymbol.Cross, Theme.PsCross, P(PhysicalButton.Cross));
        PsFace(g, new PointF(face.X - 22, face.Y), PsSymbol.Square, Theme.PsSquare, P(PhysicalButton.Square));

        Stick(g, new PointF(152, 152), _ds.LeftX, _ds.LeftY, P(PhysicalButton.L3), accent, dark: true);
        Stick(g, new PointF(248, 152), _ds.RightX, _ds.RightY, P(PhysicalButton.R3), accent, dark: true);

        // PS button and mute.
        var ps = new PointF(200, 150);
        if (P(PhysicalButton.PS))
            Glow(g, ps, 22, accent);
        using (var psFill = new SolidBrush(P(PhysicalButton.PS) ? accent : Color.FromArgb(0x1E, 0x21, 0x28)))
        using (var psPen = new Pen(Color.FromArgb(0x45, 0x4A, 0x55), 1f))
        {
            g.FillEllipse(psFill, ps.X - 9, ps.Y - 9, 18, 18);
            g.DrawEllipse(psPen, ps.X - 9, ps.Y - 9, 18, 18);
        }
        DrawCentered(g, "PS", TinyFont, P(PhysicalButton.PS) ? Color.White : Color.FromArgb(0x9A, 0xA1, 0xAD), ps);

        var mute = new RectangleF(190, 168, 20, 6);
        bool muted = P(PhysicalButton.Mute);
        if (muted)
            GlowRect(g, mute, 7, Color.FromArgb(0xFF, 0x8A, 0x3D));
        using (var mutePath = Theme.RoundedRect(mute, 3))
        using (var muteBrush = new SolidBrush(muted ? Color.FromArgb(0xFF, 0x9F, 0x5F) : Color.FromArgb(0x2C, 0x30, 0x38)))
            g.FillPath(muteBrush, mutePath);
    }

    enum PsSymbol
    {
        Triangle,
        Circle,
        Cross,
        Square,
    }

    static void PsFace(Graphics g, PointF c, PsSymbol symbol, Color color, bool pressed)
    {
        const float r = 11.5f;
        if (pressed)
            Glow(g, c, r * 2.4f, color, 210);

        var rect = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
        using (var fill = new LinearGradientBrush(rect,
                   pressed ? ControlPaint.Light(color, 0.35f) : Color.FromArgb(0xEC, 0xEE, 0xF2),
                   pressed ? color : Color.FromArgb(0xC6, 0xCA, 0xD2), LinearGradientMode.Vertical))
        using (var rim = new Pen(pressed ? ControlPaint.Light(color, 0.5f) : Color.FromArgb(0xAE, 0xB3, 0xBC), 1.2f))
        {
            g.FillEllipse(fill, rect);
            g.DrawEllipse(rim, rect);
        }

        using var pen = new Pen(pressed ? Color.White : Color.FromArgb(0x86, 0x8E, 0x9B), 1.9f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (symbol)
        {
            case PsSymbol.Triangle:
                g.DrawPolygon(pen, new PointF[] { new(c.X, c.Y - 5.2f), new(c.X + 5.2f, c.Y + 3.6f), new(c.X - 5.2f, c.Y + 3.6f) });
                break;
            case PsSymbol.Circle:
                g.DrawEllipse(pen, c.X - 5, c.Y - 5, 10, 10);
                break;
            case PsSymbol.Cross:
                g.DrawLine(pen, c.X - 4.5f, c.Y - 4.5f, c.X + 4.5f, c.Y + 4.5f);
                g.DrawLine(pen, c.X + 4.5f, c.Y - 4.5f, c.X - 4.5f, c.Y + 4.5f);
                break;
            case PsSymbol.Square:
                g.DrawRectangle(pen, c.X - 4.3f, c.Y - 4.3f, 8.6f, 8.6f);
                break;
        }
    }

    static void PsDpad(Graphics g, PointF c, bool up, bool down, bool left, bool right, Color accent)
    {
        // Four separate arrow keys pointing at the center, like the DualSense.
        void Key(PointF[] shape, PointF center, bool pressed, float angle)
        {
            if (pressed)
                Glow(g, center, 20, accent);
            using var path = new GraphicsPath();
            path.AddPolygon(shape);
            var b = path.GetBounds();
            using var fill = new LinearGradientBrush(RectangleF.Inflate(b, 1, 1),
                pressed ? ControlPaint.Light(accent, 0.3f) : Color.FromArgb(0xEE, 0xF0, 0xF3),
                pressed ? accent : Color.FromArgb(0xCB, 0xCF, 0xD6), LinearGradientMode.Vertical);
            using var rim = new Pen(pressed ? ControlPaint.Light(accent, 0.4f) : Color.FromArgb(0xAE, 0xB3, 0xBC), 1f) { LineJoin = LineJoin.Round };
            g.FillPath(fill, path);
            g.DrawPath(rim, path);

            // Small arrow glyph pointing outward.
            var state = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(angle);
            using var glyph = new SolidBrush(pressed ? Color.White : Color.FromArgb(0x9A, 0xA1, 0xAD));
            g.FillPolygon(glyph, new PointF[] { new(0, -3.2f), new(3, 1.6f), new(-3, 1.6f) });
            g.Restore(state);
        }

        float x = c.X, y = c.Y;
        Key(new PointF[] { new(x - 8, y - 25), new(x + 8, y - 25), new(x + 8, y - 12), new(x, y - 5), new(x - 8, y - 12) }, new(x, y - 16), up, 0);
        Key(new PointF[] { new(x - 8, y + 25), new(x + 8, y + 25), new(x + 8, y + 12), new(x, y + 5), new(x - 8, y + 12) }, new(x, y + 16), down, 180);
        Key(new PointF[] { new(x - 25, y - 8), new(x - 25, y + 8), new(x - 12, y + 8), new(x - 5, y), new(x - 12, y - 8) }, new(x - 16, y), left, 270);
        Key(new PointF[] { new(x + 25, y - 8), new(x + 25, y + 8), new(x + 12, y + 8), new(x + 5, y), new(x + 12, y - 8) }, new(x + 16, y), right, 90);
    }

    // ------------------------------------------------------------------ Xbox 360

    void DrawXbox360(Graphics g)
    {
        bool P(X360Buttons b) => _xb.IsPressed(b);
        var accent = Theme.XbGreen;

        Trigger(g, new RectangleF(78, 28, 60, 30), "LT", _xb.LeftTrigger, Color.FromArgb(0x9C, 0xA2, 0xAB), accent);
        Trigger(g, new RectangleF(262, 28, 60, 30), "RT", _xb.RightTrigger, Color.FromArgb(0x9C, 0xA2, 0xAB), accent);
        Bumper(g, new RectangleF(70, 44, 72, 16), "LB", P(X360Buttons.LeftShoulder), Color.FromArgb(0xF1, 0xF2, 0xF4), accent);
        Bumper(g, new RectangleF(258, 44, 72, 16), "RB", P(X360Buttons.RightShoulder), Color.FromArgb(0xF1, 0xF2, 0xF4), accent);

        using var body = Mirrored(Xbox360Outline);
        PaintBody(g, body, Color.FromArgb(0xF8, 0xF9, 0xFB), Color.FromArgb(0xD0, 0xD3, 0xD9));

        // Xbox 360 layout: left stick high, D-pad low; face buttons high, right stick low.
        Stick(g, new PointF(106, 108), _xb.ThumbLX, _xb.ThumbLY, P(X360Buttons.LeftThumb), accent, dark: false);
        Stick(g, new PointF(248, 160), _xb.ThumbRX, _xb.ThumbRY, P(X360Buttons.RightThumb), accent, dark: false);
        XboxDpad(g, new PointF(152, 160), P(X360Buttons.DpadUp), P(X360Buttons.DpadDown),
            P(X360Buttons.DpadLeft), P(X360Buttons.DpadRight), accent);

        var face = new PointF(296, 108);
        XboxFace(g, new PointF(face.X, face.Y - 22), "Y", Theme.XbY, P(X360Buttons.Y));
        XboxFace(g, new PointF(face.X + 22, face.Y), "B", Theme.XbB, P(X360Buttons.B));
        XboxFace(g, new PointF(face.X, face.Y + 22), "A", Theme.XbA, P(X360Buttons.A));
        XboxFace(g, new PointF(face.X - 22, face.Y), "X", Theme.XbX, P(X360Buttons.X));

        Guide(g, new PointF(200, 100), P(X360Buttons.Guide));
        RoundKey(g, new PointF(168, 104), P(X360Buttons.Back), accent, pointRight: false);
        RoundKey(g, new PointF(232, 104), P(X360Buttons.Start), accent, pointRight: true);
    }

    static void XboxFace(Graphics g, PointF c, string letter, Color color, bool pressed)
    {
        const float r = 11.5f;
        if (pressed)
            Glow(g, c, r * 2.5f, color, 220);

        var rect = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
        var top = pressed ? ControlPaint.Light(color, 0.55f) : ControlPaint.Light(color, 0.25f);
        var bottom = pressed ? ControlPaint.Light(color, 0.15f) : ControlPaint.Dark(color, 0.12f);
        using (var fill = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.Vertical))
        using (var rim = new Pen(pressed ? Color.White : ControlPaint.Dark(color, 0.25f), pressed ? 1.8f : 1f))
        {
            g.FillEllipse(fill, rect);
            g.DrawEllipse(rim, rect);
        }
        // Glossy highlight.
        using (var gloss = new SolidBrush(Color.FromArgb(pressed ? 120 : 80, Color.White)))
            g.FillEllipse(gloss, c.X - r * 0.62f, c.Y - r * 0.85f, r * 1.24f, r * 0.7f);
        DrawCentered(g, letter, FaceFont, Color.White, new PointF(c.X, c.Y + 0.5f));
    }

    static void XboxDpad(Graphics g, PointF c, bool up, bool down, bool left, bool right, Color accent)
    {
        using (var disc = new LinearGradientBrush(new RectangleF(c.X - 22, c.Y - 22, 44, 44),
                   Color.FromArgb(0xD8, 0xDB, 0xE0), Color.FromArgb(0xA9, 0xAE, 0xB6), LinearGradientMode.Vertical))
        using (var discPen = new Pen(Color.FromArgb(0x96, 0x9C, 0xA6), 1f))
        {
            g.FillEllipse(disc, c.X - 22, c.Y - 22, 44, 44);
            g.DrawEllipse(discPen, c.X - 22, c.Y - 22, 44, 44);
        }

        const float arm = 6.5f, len = 18f;
        void Arm(RectangleF r, bool pressed, PointF glowAt)
        {
            if (pressed)
                Glow(g, glowAt, 16, accent);
            using var path = Theme.RoundedRect(r, 2.5f);
            using var brush = new SolidBrush(pressed ? accent : Color.FromArgb(0x7E, 0x85, 0x90));
            g.FillPath(brush, path);
        }
        using (var hub = new SolidBrush(Color.FromArgb(0x7E, 0x85, 0x90)))
            g.FillRectangle(hub, c.X - arm, c.Y - arm, arm * 2, arm * 2);
        Arm(new RectangleF(c.X - arm, c.Y - len, arm * 2, len - arm + 1), up, new(c.X, c.Y - 12));
        Arm(new RectangleF(c.X - arm, c.Y + arm - 1, arm * 2, len - arm + 1), down, new(c.X, c.Y + 12));
        Arm(new RectangleF(c.X - len, c.Y - arm, len - arm + 1, arm * 2), left, new(c.X - 12, c.Y));
        Arm(new RectangleF(c.X + arm - 1, c.Y - arm, len - arm + 1, arm * 2), right, new(c.X + 12, c.Y));
    }

    static void Guide(Graphics g, PointF c, bool pressed)
    {
        if (pressed)
            Glow(g, c, 34, Theme.XbGreen, 230);

        using (var silver = new LinearGradientBrush(new RectangleF(c.X - 16, c.Y - 16, 32, 32),
                   Color.FromArgb(0xF2, 0xF4, 0xF6), Color.FromArgb(0x93, 0x99, 0xA3), LinearGradientMode.ForwardDiagonal))
            g.FillEllipse(silver, c.X - 16, c.Y - 16, 32, 32);

        // Ring of light: four quadrants, the player-1 quadrant brightest.
        for (int q = 0; q < 4; q++)
        {
            int alpha = pressed ? 255 : q == 0 ? 230 : 90;
            using var ring = new Pen(Color.FromArgb(alpha, Theme.XbGreen), 3f);
            g.DrawArc(ring, c.X - 12.5f, c.Y - 12.5f, 25, 25, 180 + q * 90 + 8, 74);
        }

        using (var inner = new SolidBrush(pressed ? Color.FromArgb(0x2E, 0x6B, 0x22) : Color.FromArgb(0x22, 0x27, 0x2E)))
            g.FillEllipse(inner, c.X - 9.5f, c.Y - 9.5f, 19, 19);
        using var mark = new Pen(pressed ? Color.White : Color.FromArgb(0xB7, 0xE8, 0xA2), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(mark, c.X - 4.5f, c.Y - 4.5f, c.X + 4.5f, c.Y + 4.5f);
        g.DrawLine(mark, c.X + 4.5f, c.Y - 4.5f, c.X - 4.5f, c.Y + 4.5f);
    }

    static void RoundKey(Graphics g, PointF c, bool pressed, Color accent, bool pointRight)
    {
        const float r = 6f;
        if (pressed)
            Glow(g, c, 16, accent);
        using (var fill = new SolidBrush(pressed ? accent : Color.FromArgb(0x2F, 0x33, 0x3B)))
        using (var rim = new Pen(Color.FromArgb(0x9A, 0xA0, 0xA9), 1f))
        {
            g.FillEllipse(fill, c.X - r, c.Y - r, r * 2, r * 2);
            g.DrawEllipse(rim, c.X - r, c.Y - r, r * 2, r * 2);
        }
        float d = pointRight ? 1 : -1;
        using var glyph = new SolidBrush(Color.FromArgb(0xE6, 0xE8, 0xEC));
        g.FillPolygon(glyph, new PointF[] { new(c.X + 2.8f * d, c.Y), new(c.X - 1.8f * d, c.Y - 2.8f), new(c.X - 1.8f * d, c.Y + 2.8f) });
    }

    // ------------------------------------------------------------------ shared parts

    static void Trigger(Graphics g, RectangleF r, string label, byte value, Color baseColor, Color accent)
    {
        using var path = Theme.RoundedRect(r, 11);
        using (var fill = new LinearGradientBrush(r, ControlPaint.Light(baseColor, 0.25f), baseColor, LinearGradientMode.Vertical))
            g.FillPath(fill, path);

        if (value > 0)
        {
            var state = g.Save();
            g.SetClip(path);
            float w = r.Width * value / 255f;
            using var level = new LinearGradientBrush(new RectangleF(r.X, r.Y, Math.Max(1, w), r.Height),
                ControlPaint.Light(accent, 0.3f), accent, LinearGradientMode.Vertical);
            g.FillRectangle(level, r.X, r.Y, w, r.Height);
            g.Restore(state);
            if (value > 200)
                GlowRect(g, r, 8, accent, 120);
        }

        bool lightBase = baseColor.GetBrightness() > 0.5f;
        var text = value > 90 ? Color.White : lightBase ? Color.FromArgb(0x3A, 0x3F, 0x48) : Color.FromArgb(0xA8, 0xAF, 0xBB);
        DrawCentered(g, label, LabelFont, text, new PointF(r.X + r.Width / 2, r.Y + 8));
    }

    static void Bumper(Graphics g, RectangleF r, string label, bool pressed, Color baseColor, Color accent)
    {
        if (pressed)
            GlowRect(g, r, 9, accent);
        using var path = Theme.RoundedRect(r, r.Height / 2);
        using (var fill = new LinearGradientBrush(r,
                   pressed ? ControlPaint.Light(accent, 0.35f) : Color.White,
                   pressed ? accent : baseColor, LinearGradientMode.Vertical))
        using (var rim = new Pen(pressed ? ControlPaint.Light(accent, 0.4f) : Color.FromArgb(0xB6, 0xBB, 0xC4), 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(rim, path);
        }
        DrawCentered(g, label, TinyFont, pressed ? Color.White : Color.FromArgb(0x6B, 0x72, 0x7E), new PointF(r.X + r.Width / 2, r.Y + 6));
    }

    static void Stick(Graphics g, PointF c, short x, short y, bool pressed, Color accent, bool dark)
    {
        const float well = 25, cap = 18, travel = 10;
        if (pressed)
            Glow(g, c, well + 12, accent);

        using (var wellBrush = new SolidBrush(dark ? Color.FromArgb(0x06, 0x07, 0x09) : Color.FromArgb(0x2A, 0x2D, 0x33)))
        using (var wellPen = new Pen(dark ? Color.FromArgb(0x30, 0x34, 0x3C) : Color.FromArgb(0xA4, 0xA9, 0xB2), 1.2f))
        {
            g.FillEllipse(wellBrush, c.X - well, c.Y - well, well * 2, well * 2);
            g.DrawEllipse(wellPen, c.X - well, c.Y - well, well * 2, well * 2);
        }

        // Y is "up = positive", screen Y grows downward.
        var k = new PointF(c.X + x / 32768f * travel, c.Y - y / 32768f * travel);
        var capRect = new RectangleF(k.X - cap, k.Y - cap, cap * 2, cap * 2);
        using (var capFill = new LinearGradientBrush(capRect,
                   pressed ? ControlPaint.Light(accent, 0.2f) : Color.FromArgb(0x4A, 0x4F, 0x59),
                   pressed ? ControlPaint.Dark(accent, 0.2f) : Color.FromArgb(0x16, 0x18, 0x1C), LinearGradientMode.Vertical))
        using (var rim = new Pen(pressed ? ControlPaint.Light(accent, 0.5f) : Color.FromArgb(0x5A, 0x60, 0x6B), pressed ? 2.2f : 1.2f))
        {
            g.FillEllipse(capFill, capRect);
            g.DrawEllipse(rim, capRect);
        }
        // Concave top with grip ring.
        using (var dish = new SolidBrush(pressed ? ControlPaint.Dark(accent, 0.1f) : Color.FromArgb(0x22, 0x25, 0x2B)))
            g.FillEllipse(dish, k.X - 12, k.Y - 12, 24, 24);
        using (var grip = new Pen(Color.FromArgb(pressed ? 140 : 70, Color.White), 0.8f) { DashPattern = new[] { 1.5f, 2.2f } })
            g.DrawEllipse(grip, k.X - 15, k.Y - 15, 30, 30);
    }

    static void SmallKey(Graphics g, RectangleF r, bool pressed, Color accent)
    {
        if (pressed)
            GlowRect(g, r, 8, accent);
        using var path = Theme.RoundedRect(r, r.Width / 2);
        using var fill = new SolidBrush(pressed ? accent : Color.FromArgb(0x2C, 0x30, 0x38));
        using var rim = new Pen(Color.FromArgb(0x50, 0x56, 0x60), 1f);
        g.FillPath(fill, path);
        g.DrawPath(rim, path);
    }

    static GraphicsPath Mirrored(PointF[] left)
    {
        var points = new List<PointF>(left);
        for (int i = left.Length - 2; i >= 0; i--)
            points.Add(new PointF(W - left[i].X, left[i].Y));
        var path = new GraphicsPath();
        path.AddBeziers(points.ToArray());
        path.CloseFigure();
        return path;
    }

    static void PaintBody(Graphics g, GraphicsPath body, Color top, Color bottom)
    {
        // Soft drop shadow: a few offset copies at low opacity.
        for (int i = 5; i >= 1; i--)
        {
            using var shadow = (GraphicsPath)body.Clone();
            using var m = new Matrix();
            m.Translate(0, i * 2.2f);
            shadow.Transform(m);
            using var brush = new SolidBrush(Color.FromArgb(34, 0, 0, 0));
            g.FillPath(brush, shadow);
        }

        var bounds = body.GetBounds();
        using (var fill = new LinearGradientBrush(bounds, top, bottom, LinearGradientMode.Vertical))
            g.FillPath(fill, body);

        // Top-light sheen and a crisp rim.
        var state = g.Save();
        g.SetClip(body);
        using (var sheen = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height * 0.45f),
                   Color.FromArgb(70, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
            g.FillRectangle(sheen, bounds.X, bounds.Y, bounds.Width, bounds.Height * 0.45f);
        g.Restore(state);
        using var rim = new Pen(Color.FromArgb(110, 0x9A, 0xA0, 0xAA), 1f);
        g.DrawPath(rim, body);
    }

    static void Glow(Graphics g, PointF c, float r, Color color, int alpha = 190)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(alpha, color),
            SurroundColors = new[] { Color.FromArgb(0, color) },
        };
        g.FillPath(brush, path);
    }

    static void GlowRect(Graphics g, RectangleF r, float spread, Color color, int alpha = 170)
    {
        var outer = RectangleF.Inflate(r, spread, spread);
        using var path = Theme.RoundedRect(outer, Math.Min(outer.Width, outer.Height) / 2);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(alpha, color),
            SurroundColors = new[] { Color.FromArgb(0, color) },
            FocusScales = new PointF(Math.Clamp(r.Width / outer.Width, 0, 1), Math.Clamp(r.Height / outer.Height, 0, 1)),
        };
        g.FillPath(brush, path);
    }

    static void DrawCentered(Graphics g, string text, Font font, Color color, PointF center)
    {
        var size = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, center.X - size.Width / 2, center.Y - size.Height / 2, StringFormat.GenericTypographic);
    }
}
