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

    // Left half of the outline, top-center to bottom-center, as cubic Bezier points; mirrored for the right.
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

    /// <summary>Where a button sits (center and radius) in photo-reference or design coordinates.</summary>
    readonly record struct Spot(float X, float Y, float R);

    // Positions on the 1600x1600 reference photos (see the note above DsRefW).
    static readonly Dictionary<PhysicalButton, Spot> DsPhotoSpots = new()
    {
        [PhysicalButton.Cross] = new(1058, 758, 27), [PhysicalButton.Circle] = new(1117, 699, 27),
        [PhysicalButton.Square] = new(998, 699, 27), [PhysicalButton.Triangle] = new(1058, 639, 27),
        [PhysicalButton.L1] = new(550, 556, 40), [PhysicalButton.R1] = new(1060, 556, 40),
        [PhysicalButton.L3] = new(675, 809, 52), [PhysicalButton.R3] = new(937, 809, 52),
        [PhysicalButton.Options] = new(994, 605, 16), [PhysicalButton.Create] = new(614, 605, 16),
        [PhysicalButton.PS] = new(805, 805, 30), [PhysicalButton.Touchpad] = new(803, 642, 70),
        [PhysicalButton.Mute] = new(805, 853, 18),
        [PhysicalButton.DpadUp] = new(551, 656, 25), [PhysicalButton.DpadDown] = new(551, 745, 25),
        [PhysicalButton.DpadLeft] = new(505, 699, 25), [PhysicalButton.DpadRight] = new(597, 699, 25),
    };

    static readonly Dictionary<X360Buttons, Spot> XbPhotoSpots = new()
    {
        [X360Buttons.A] = new(1120, 692, 44), [X360Buttons.B] = new(1205, 607, 44),
        [X360Buttons.X] = new(1034, 607, 44), [X360Buttons.Y] = new(1120, 524, 44),
        [X360Buttons.LeftShoulder] = new(493, 384, 60), [X360Buttons.RightShoulder] = new(1122, 384, 60),
        [X360Buttons.LeftThumb] = new(476, 610, 72), [X360Buttons.RightThumb] = new(960, 782, 72),
        [X360Buttons.Start] = new(891, 606, 28), [X360Buttons.Back] = new(709, 606, 28),
        [X360Buttons.Guide] = new(801, 479, 48),
        [X360Buttons.DpadUp] = new(636, 743, 30), [X360Buttons.DpadDown] = new(636, 866, 30),
        [X360Buttons.DpadLeft] = new(575, 803, 30), [X360Buttons.DpadRight] = new(698, 803, 30),
    };

    // Positions in the 400x270 design space of the built-in drawings.
    static readonly Dictionary<PhysicalButton, Spot> DsDrawSpots = new()
    {
        [PhysicalButton.Cross] = new(265, 122, 11), [PhysicalButton.Circle] = new(288, 102, 11),
        [PhysicalButton.Square] = new(242, 102, 11), [PhysicalButton.Triangle] = new(265, 82, 11),
        [PhysicalButton.L1] = new(68, 38, 13), [PhysicalButton.R1] = new(259, 38, 13),
        [PhysicalButton.L3] = new(112, 143, 19), [PhysicalButton.R3] = new(215, 143, 19),
        [PhysicalButton.Options] = new(240, 71, 7), [PhysicalButton.Create] = new(87, 71, 7),
        [PhysicalButton.PS] = new(163.5f, 134, 9), [PhysicalButton.Touchpad] = new(163.5f, 75, 30),
        [PhysicalButton.Mute] = new(163.5f, 146.5f, 6),
        [PhysicalButton.DpadUp] = new(62, 88, 9), [PhysicalButton.DpadDown] = new(62, 116, 9),
        [PhysicalButton.DpadLeft] = new(48, 102, 9), [PhysicalButton.DpadRight] = new(76, 102, 9),
    };

    static readonly Dictionary<X360Buttons, Spot> XbDrawSpots = new()
    {
        [X360Buttons.A] = new(296, 130, 12), [X360Buttons.B] = new(318, 108, 12),
        [X360Buttons.X] = new(274, 108, 12), [X360Buttons.Y] = new(296, 86, 12),
        [X360Buttons.LeftShoulder] = new(106, 52, 16), [X360Buttons.RightShoulder] = new(294, 52, 16),
        [X360Buttons.LeftThumb] = new(106, 108, 20), [X360Buttons.RightThumb] = new(248, 160, 20),
        [X360Buttons.Start] = new(232, 104, 8), [X360Buttons.Back] = new(168, 104, 8),
        [X360Buttons.Guide] = new(200, 100, 15),
        [X360Buttons.DpadUp] = new(152, 148, 8), [X360Buttons.DpadDown] = new(152, 172, 8),
        [X360Buttons.DpadLeft] = new(140, 160, 8), [X360Buttons.DpadRight] = new(164, 160, 8),
    };

    PadState _ds;
    X360Report _xb;
    bool _active;
    PadKind _kind;
    PhotoSkin? _dsSkin;
    PhotoSkin? _xbSkin;
    PhysicalButton? _highlight;
    X360Buttons _highlightXbox;

    Bitmap? _photoCache;
    (int, int, PhotoSkin, Color) _photoCacheKey;

    // How the last paint mapped spot coordinates to pixels (photo or design space).
    Func<float, float, PointF> _mapPoint = (x, y) => new PointF(x, y);
    Func<float, float> _mapRadius = r => r;

    public PadView(PadKind kind)
    {
        _kind = kind;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        MinimumSize = new Size(240, 160);
    }

    /// <summary>Which controller is shown: the physical DualSense or the virtual Xbox pad.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public PadKind Kind
    {
        get => _kind;
        set { _kind = value; Invalidate(); }
    }

    /// <summary>The product photo for the shown kind; null for the built-in artwork.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public PhotoSkin? Skin
    {
        get => _kind == PadKind.DualSense ? _dsSkin : _xbSkin;
        set => SetSkin(_kind, value);
    }

    public void SetSkin(PadKind kind, PhotoSkin? skin)
    {
        if (kind == PadKind.DualSense)
            _dsSkin = skin;
        else
            _xbSkin = skin;
        Invalidate();
    }

    public PhotoSkin? GetSkin(PadKind kind) => kind == PadKind.DualSense ? _dsSkin : _xbSkin;

    /// <summary>Dims the picture with "No controller connected" while nothing is connected.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowDisconnectedVeil { get; set; } = true;

    /// <summary>Lets the user click buttons on the picture (raises <see cref="PhysicalButtonClicked"/> / <see cref="XboxButtonClicked"/>).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Clickable { get; set; }

    public event Action<PhysicalButton>? PhysicalButtonClicked;
    public event Action<X360Buttons>? XboxButtonClicked;

    /// <summary>Selection ring on a DualSense button (DualSense view).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public PhysicalButton? Highlight
    {
        get => _highlight;
        set { _highlight = value; Invalidate(); }
    }

    /// <summary>Selection rings on Xbox buttons (Xbox view).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public X360Buttons HighlightXbox
    {
        get => _highlightXbox;
        set { _highlightXbox = value; Invalidate(); }
    }

    public void SetState(in PadState physical, in X360Report xbox, bool active)
    {
        if (_active == active && _ds == physical && _xb == xbox)
            return;
        // Ignore stick jitter too small to see (< ~1% of travel) so a resting controller doesn't repaint constantly.
        static bool Near(short a, short b) => Math.Abs(a - b) < 300;
        if (_active == active && _ds.Buttons == physical.Buttons && _ds.L2 == physical.L2 && _ds.R2 == physical.R2 &&
            _xb.Buttons == xbox.Buttons && _xb.LeftTrigger == xbox.LeftTrigger && _xb.RightTrigger == xbox.RightTrigger &&
            Near(_ds.LeftX, physical.LeftX) && Near(_ds.LeftY, physical.LeftY) && Near(_ds.RightX, physical.RightX) && Near(_ds.RightY, physical.RightY) &&
            Near(_xb.ThumbLX, xbox.ThumbLX) && Near(_xb.ThumbLY, xbox.ThumbLY) && Near(_xb.ThumbRX, xbox.ThumbRX) && Near(_xb.ThumbRY, xbox.ThumbRY))
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

        if (Skin is { } skin)
        {
            DrawPhoto(g, skin);
        }
        else
        {
            var state = g.Save();
            bool ds = _kind == PadKind.DualSense;
            float dw = ds ? DsW : W, dh = ds ? DsH : H;
            float s = Math.Min(Width / dw, Height / dh);
            float ox = (Width - dw * s) / 2, oy = (Height - dh * s) / 2;
            _mapPoint = (x, y) => new PointF(ox + x * s, oy + y * s);
            _mapRadius = r => r * s;
            g.TranslateTransform(ox, oy);
            g.ScaleTransform(s, s);
            if (_kind == PadKind.DualSense)
                DrawDualSense(g);
            else
                DrawXbox360(g);
            g.Restore(state);
        }

        if (!_active && ShowDisconnectedVeil)
        {
            using var veil = new SolidBrush(Color.FromArgb(200, BackColor));
            g.FillRectangle(veil, ClientRectangle);
            DrawCentered(g, "No controller connected", OverlayFont, Theme.Muted, new PointF(Width / 2f, Height / 2f));
        }

        DrawSelection(g);
    }

    Dictionary<PhysicalButton, Spot> DsSpots => _dsSkin is not null ? DsPhotoSpots : DsDrawSpots;
    Dictionary<X360Buttons, Spot> XbSpots => _xbSkin is not null ? XbPhotoSpots : XbDrawSpots;

    void DrawSelection(Graphics g)
    {
        void Ring(Spot spot)
        {
            var c = _mapPoint(spot.X, spot.Y);
            float r = Math.Max(_mapRadius(spot.R) * 1.3f, 9f);
            using var glow = new Pen(Color.FromArgb(90, Theme.Accent), 6f);
            using var pen = new Pen(Theme.Accent, 2.2f);
            g.DrawEllipse(glow, c.X - r, c.Y - r, r * 2, r * 2);
            g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
        }

        if (_kind == PadKind.DualSense && _highlight is { } b && DsSpots.TryGetValue(b, out var spot))
            Ring(spot);
        if (_kind == PadKind.Xbox && _highlightXbox != X360Buttons.None)
        {
            foreach (var (button, xs) in XbSpots)
            {
                if ((_highlightXbox & button) == button)
                    Ring(xs);
            }
        }
    }

    /// <summary>The button under a point in the current view, if any.</summary>
    public (PhysicalButton? Physical, X360Buttons Xbox) HitTest(Point p)
    {
        float best = float.MaxValue;
        PhysicalButton? physical = null;
        X360Buttons xbox = X360Buttons.None;

        bool Inside(Spot s, out float d)
        {
            var c = _mapPoint(s.X, s.Y);
            float r = Math.Max(_mapRadius(s.R) * 1.15f, 10f);
            d = MathF.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y));
            return d <= r;
        }

        if (_kind == PadKind.DualSense)
        {
            foreach (var (button, spot) in DsSpots)
            {
                if (Inside(spot, out float d) && d < best) { best = d; physical = button; }
            }
        }
        else
        {
            foreach (var (button, spot) in XbSpots)
            {
                if (Inside(spot, out float d) && d < best) { best = d; xbox = button; }
            }
        }
        return (physical, xbox);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Clickable)
        {
            var (p, x) = HitTest(e.Location);
            Cursor = p is not null || x != X360Buttons.None ? Cursors.Hand : Cursors.Default;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (Clickable && e.Button == MouseButtons.Left)
        {
            var (p, x) = HitTest(e.Location);
            if (p is { } physical)
                PhysicalButtonClicked?.Invoke(physical);
            else if (x != X360Buttons.None)
                XboxButtonClicked?.Invoke(x);
        }
        base.OnMouseClick(e);
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

        // The halo and the bicubic-scaled photo only change with size or skin, so render them
        // once into a cache; a frame then costs a single unscaled blit plus the highlights.
        var key = (Width, Height, skin, BackColor);
        if (_photoCache is null || _photoCacheKey != key)
        {
            _photoCache?.Dispose();
            _photoCache = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            using var cg = Graphics.FromImage(_photoCache);
            cg.SmoothingMode = SmoothingMode.AntiAlias;
            cg.InterpolationMode = InterpolationMode.HighQualityBicubic;
            cg.Clear(BackColor);
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
                cg.FillPath(brush, halo);
            }
            cg.DrawImage(skin.Image, dest);
            _photoCacheKey = key;
        }
        g.DrawImageUnscaled(_photoCache, 0, 0);

        bool ds = Kind == PadKind.DualSense;
        float refW = ds ? DsRefW : XbRefW, refH = ds ? DsRefH : XbRefH;
        PointF P(float x, float y) => new(
            dest.X + (x / refW * skin.Source.Width - skin.Crop.X) * scale,
            dest.Y + (y / refH * skin.Source.Height - skin.Crop.Y) * scale);
        float R(float r) => r / refW * skin.Source.Width * scale;
        _mapPoint = P;
        _mapRadius = R;
        RectangleF Bar(float centerX) => new(P(centerX, 0).X - R(ds ? 80 : 110), band * 0.28f, R(ds ? 160 : 220), Math.Max(10, band * 0.42f));

        if (ds)
        {
            bool B(PhysicalButton b) => _ds.IsPressed(b);
            var accent = Theme.PsBlue;
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

    /// <summary>
    /// What each DualSense button sends, so a pressed face button lights up in the color of its
    /// Xbox button (Cross → A shows green). Null: PlayStation blue for everything.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ButtonMap? Map
    {
        get => _map;
        set { _map = value; Invalidate(); }
    }

    ButtonMap? _map;

    // The DualSense drawing has its own design space (wider and flatter than the Xbox one).
    const float DsW = 327, DsH = 192, DsCx = DsW / 2;

    static readonly Color DsShellTop = Color.FromArgb(0xFB, 0xFC, 0xFE);
    static readonly Color DsShellBottom = Color.FromArgb(0xD9, 0xDE, 0xE6);
    static readonly Color DsSymbol = Color.FromArgb(0xD2, 0xD5, 0xDA);
    static readonly Color DsMeterRed = Color.FromArgb(0xE0, 0x4F, 0x55);
    static readonly Color DsLightBar = Color.FromArgb(0x2E, 0x78, 0xD8);
    static readonly Color DsRing = Color.FromArgb(0x4B, 0x3B, 0xF0);

    // Left half of the shell, top center to bottom center (cubic Beziers); mirrored for the right.
    // Measured from the reference: outer edge x 37@40, 20@80, 9@120-140, 15@164, 26@180, tip ~ (36, 187).
    static readonly PointF[] DsShellOutline =
    {
        new(DsCx, 38),
        new(110, 38), new(56, 37), new(40, 41),
        new(31, 47), new(23, 63), new(18, 84),
        new(13, 104), new(8, 124), new(9, 146),
        new(10, 164), new(21, 180), new(32, 186),
        new(40, 189), new(49, 184), new(58, 176),
        new(64, 167), new(70, 157), new(82, 151),
        new(110, 150), new(140, 151), new(DsCx, 151),
    };

    Color PressColor(PhysicalButton b)
    {
        var target = _map?[b] ?? X360Buttons.None;
        return target switch
        {
            X360Buttons.A => Color.FromArgb(0x8F, 0xD4, 0x00),
            X360Buttons.B => Theme.XbB,
            X360Buttons.X => Theme.XbX,
            X360Buttons.Y => Theme.XbY,
            _ => Theme.PsBlue,
        };
    }

    static PointF Mirror(PointF p) => new(DsW - p.X, p.Y);

    static PointF[] Mirror(PointF[] points) => points.Select(Mirror).ToArray();

    /// <summary>Soft shadow of a shape: a few offset copies at low opacity.</summary>
    static void DropShadow(Graphics g, GraphicsPath path, float dy, int alpha, int steps = 4)
    {
        for (int i = steps; i >= 1; i--)
        {
            using var copy = (GraphicsPath)path.Clone();
            using var m = new Matrix();
            m.Translate(0, dy * i / steps);
            copy.Transform(m);
            using var brush = new SolidBrush(Color.FromArgb(alpha / steps, 0, 0, 0));
            g.FillPath(brush, copy);
        }
    }

    static void FillVertical(Graphics g, GraphicsPath path, Color top, Color bottom)
    {
        var b = path.GetBounds();
        using var brush = new LinearGradientBrush(new RectangleF(b.X, b.Y - 1, b.Width, b.Height + 2), top, bottom, LinearGradientMode.Vertical);
        g.FillPath(brush, path);
    }

    /// <summary>Flat-shaded DualSense: white wings, black center and touchpad, black shoulders.</summary>
    void DrawDualSense(Graphics g)
    {
        bool P(PhysicalButton b) => _ds.IsPressed(b);

        // Soft violet halo so the black parts stand out from a black window (kept inside the design box).
        using (var halo = new GraphicsPath())
        {
            halo.AddEllipse(-24, 2, DsW + 48, DsH - 4);
            using var brush = new PathGradientBrush(halo)
            {
                CenterColor = Color.FromArgb(0x24, 0x1E, 0x33),
                SurroundColors = new[] { Color.FromArgb(0, 0x24, 0x1E, 0x33) },
                FocusScales = new PointF(0.35f, 0.3f),
            };
            g.FillPath(brush, halo);
        }

        DsMeter(g, new RectangleF(9, 24, 10, 28), _ds.L2, barsRight: true);
        DsMeter(g, new RectangleF(DsW - 19, 24, 10, 28), _ds.R2, barsRight: false);

        using var shell = MirroredPath(DsShellOutline, DsW);
        DropShadow(g, shell, 5, 120, 5);

        // Shell: lit from above, falling off toward the grips, with a darker band around the edge
        // so the wings read as rounded.
        FillVertical(g, shell, DsShellTop, DsShellBottom);
        var clip = g.Save();
        g.SetClip(shell);
        using (var edge = new Pen(Color.FromArgb(38, 0x5A, 0x64, 0x78), 7f))
            g.DrawPath(edge, shell);
        using (var edge2 = new Pen(Color.FromArgb(30, 0x5A, 0x64, 0x78), 3f))
            g.DrawPath(edge2, shell);
        foreach (bool right in new[] { false, true })
        {
            // Highlight on the upper wing.
            var hi = new RectangleF(right ? DsW - 70 : 22, 52, 48, 60);
            using var hiPath = new GraphicsPath();
            hiPath.AddEllipse(hi);
            using var hiBrush = new PathGradientBrush(hiPath)
            {
                CenterColor = Color.FromArgb(150, Color.White),
                SurroundColors = new[] { Color.FromArgb(0, Color.White) },
            };
            g.FillPath(hiBrush, hiPath);
        }

        // Black center (touchpad and everything under it) and the black strips down the inside of
        // each grip, clipped to the shell.
        var centerLeft = new PointF[]
        {
            new(DsCx, 40), new(98, 40), new(94, 42), new(94, 60),
            new(95, 90), new(94, 112), new(86, 124), new(74, 136),
            new(66, 147), new(62, 156), new(56, 166), new(46, 180), new(40, 194),
        };
        using (var center = new GraphicsPath())
        {
            // Down the left side, across below the shell, up the right side.
            var pts = new List<PointF>(centerLeft) { new(30, 205), new(DsW - 30, 205) };
            pts.AddRange(Mirror(centerLeft).Reverse().SkipLast(1));
            center.AddClosedCurve(pts.ToArray(), 0.25f);
            FillVertical(g, center, Color.FromArgb(0x26, 0x27, 0x2B), Color.FromArgb(0x13, 0x14, 0x16));
            // Thin lit edge where the black meets the white wing.
            using var lip = new Pen(Color.FromArgb(70, 0x9A, 0xA2, 0xB0), 1f);
            g.DrawPath(lip, center);
        }
        // White rim along the inner edge of each grip, inside the black strip.
        foreach (bool right in new[] { false, true })
        {
            using var rimPath = new GraphicsPath();
            var rim = new PointF[] { new(76, 152), new(70, 160), new(64, 170), new(57, 180), new(51, 191) };
            rimPath.AddCurve(right ? Mirror(rim) : rim, 0.4f);
            using var pen = new Pen(Color.FromArgb(0xE2, 0xE6, 0xEC), 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawPath(pen, rimPath);
        }
        g.Restore(clip);

        // Rim everywhere except the lower edge between the grips, which the black center owns.
        var rimClip = g.Save();
        g.ExcludeClip(Rectangle.Round(new RectangleF(74, 144, DsW - 148, 30)));
        using (var shellRim = new Pen(Color.FromArgb(0xB4, 0xBB, 0xC6), 0.9f))
            g.DrawPath(shellRim, shell);
        g.Restore(rimClip);
        // Cover the white shell's anti-aliased fringe along the black lower edge between the grips.
        using (var seam = new SolidBrush(Color.FromArgb(0x13, 0x14, 0x16)))
            g.FillRectangle(seam, 80, 148.6f, DsW - 160, 3);

        // Stick wells: a dark ring with a shadow inside its top edge.
        foreach (var c in new[] { new PointF(112, 142), new PointF(DsW - 112, 142) })
        {
            var r = new RectangleF(c.X - 24, c.Y - 22, 48, 44);
            using var wellPath = new GraphicsPath();
            wellPath.AddEllipse(r);
            FillVertical(g, wellPath, Color.FromArgb(0x16, 0x17, 0x1A), Color.FromArgb(0x33, 0x35, 0x3B));
            using var wellEdge = new Pen(Color.FromArgb(0x0E, 0x0F, 0x11), 1.2f);
            g.DrawEllipse(wellEdge, r);
        }

        // Shoulders: black caps over the top of each wing, L2 behind L1.
        DsShoulder(g, right: false, l2: _ds.L2, l1: P(PhysicalButton.L1));
        DsShoulder(g, right: true, l2: _ds.R2, l1: P(PhysicalButton.R1));

        // White strip across the top (a raised lip) with the slot in the middle.
        var strip = new RectangleF(86, 30, DsW - 172, 12);
        using (var stripPath = Theme.RoundedRect(strip, 6))
        {
            DropShadow(g, stripPath, 2.5f, 140, 3);
            FillVertical(g, stripPath, Color.White, Color.FromArgb(0xD3, 0xD8, 0xE0));
            using var stripRim = new Pen(Color.FromArgb(0xA8, 0xAF, 0xBA), 0.7f);
            g.DrawPath(stripRim, stripPath);
        }
        using (var slot = Theme.RoundedRect(new RectangleF(DsCx - 9, 33.4f, 18, 3.2f), 1.6f))
        {
            FillVertical(g, slot, Color.FromArgb(0x22, 0x23, 0x27), Color.FromArgb(0x4A, 0x4D, 0x55));
        }

        // Touchpad: dot grid on black, light bar down both sides, three marks along the bottom.
        bool touch = P(PhysicalButton.Touchpad);
        var pad = new RectangleF(97, 43, DsW - 194, 64);
        if (touch)
            GlowRect(g, pad, 6, Theme.PsBlue, 120);
        for (int row = 0; row < 13; row++)
        {
            float y = 47.5f + row * 4.5f;
            float shift = row % 2 == 0 ? 0 : 2.25f;
            // Dots fade slightly toward the bottom, like light falling across the pad.
            int a = 255 - row * 7;
            using var dot = new SolidBrush(touch ? Color.FromArgb(a, 0x8F, 0xB6, 0xFF) : Color.FromArgb(a, 0x8C, 0x8F, 0x96));
            for (float x = 101.5f + shift; x < DsW - 100; x += 4.5f)
                g.FillEllipse(dot, x - 0.85f, y - 0.85f, 1.7f, 1.7f);
        }
        foreach (bool right in new[] { false, true })
        {
            var bar = new PointF[] { new(96.5f, 43), new(96.5f, 70), new(97.5f, 95), new(101, 106) };
            using var path = new GraphicsPath();
            path.AddBeziers(right ? Mirror(bar) : bar);
            using (var glow = new Pen(Color.FromArgb(70, touch ? Theme.PsBlue : DsLightBar), 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawPath(glow, path);
            using var pen = new Pen(touch ? Theme.PsBlue : DsLightBar, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawPath(pen, path);
        }
        using (var mark = new SolidBrush(Color.FromArgb(0xEE, 0xF0, 0xF3)))
            foreach (float x in new[] { DsCx - 31, DsCx, DsCx + 31 })
                g.FillRectangle(mark, x - 2, 104.5f, 4, 2);

        // Speaker holes, PS mark and mute slot.
        using (var hole = new SolidBrush(Color.FromArgb(0x4A, 0x4C, 0x52)))
        {
            for (int i = 0; i < 6; i++)
                g.FillEllipse(hole, DsCx - 12.5f + i * 5 - 0.9f, 117.5f, 1.8f, 1.8f);
            for (int i = 0; i < 5; i++)
                g.FillEllipse(hole, DsCx - 10 + i * 5 - 0.9f, 122, 1.8f, 1.8f);
        }
        bool psOn = P(PhysicalButton.PS);
        if (psOn)
            Glow(g, new PointF(DsCx, 134), 14, Theme.PsBlue, 200);
        using (var psFont = new Font("Segoe UI Black", 10.5f, FontStyle.Italic, GraphicsUnit.Pixel))
            DrawCentered(g, "PS", psFont, psOn ? Color.White : Color.FromArgb(0x55, 0x57, 0x5E), new PointF(DsCx, 134));
        bool muted = P(PhysicalButton.Mute);
        var mute = new RectangleF(DsCx - 8, 145, 16, 3.2f);
        if (muted)
            GlowRect(g, mute, 5, Color.FromArgb(0xFF, 0x8A, 0x3D));
        using (var mutePath = Theme.RoundedRect(mute, 1.6f))
            FillVertical(g, mutePath, muted ? Color.FromArgb(0xFF, 0xB0, 0x7A) : Color.FromArgb(0x6A, 0x6D, 0x75),
                muted ? Color.FromArgb(0xFF, 0x8A, 0x3D) : Color.FromArgb(0x40, 0x42, 0x48));

        DsPill(g, new PointF(87, 71), P(PhysicalButton.Create), create: true);
        DsPill(g, new PointF(DsW - 87, 71), P(PhysicalButton.Options), create: false);

        DsDpad(g, new PointF(62, 102), P(PhysicalButton.DpadUp), P(PhysicalButton.DpadDown), P(PhysicalButton.DpadLeft), P(PhysicalButton.DpadRight));

        var face = new PointF(DsW - 62, 102);
        DsFace(g, new PointF(face.X, face.Y - 20), PsSymbol.Triangle, P(PhysicalButton.Triangle), PressColor(PhysicalButton.Triangle));
        DsFace(g, new PointF(face.X + 23, face.Y), PsSymbol.Circle, P(PhysicalButton.Circle), PressColor(PhysicalButton.Circle));
        DsFace(g, new PointF(face.X, face.Y + 20), PsSymbol.Cross, P(PhysicalButton.Cross), PressColor(PhysicalButton.Cross));
        DsFace(g, new PointF(face.X - 23, face.Y), PsSymbol.Square, P(PhysicalButton.Square), PressColor(PhysicalButton.Square));

        DsStick(g, new PointF(112, 143), _ds.LeftX, _ds.LeftY, P(PhysicalButton.L3));
        DsStick(g, new PointF(DsW - 112, 143), _ds.RightX, _ds.RightY, P(PhysicalButton.R3));
    }

    static GraphicsPath MirroredPath(PointF[] left, float width)
    {
        var points = new List<PointF>(left);
        for (int i = left.Length - 2; i >= 0; i--)
            points.Add(new PointF(width - left[i].X, left[i].Y));
        var path = new GraphicsPath();
        path.AddBeziers(points.ToArray());
        path.CloseFigure();
        return path;
    }

    /// <summary>Trigger meter beside the controller: a pill that fills from the top as L2/R2 is pulled, plus three bars.</summary>
    static void DsMeter(Graphics g, RectangleF r, byte value, bool barsRight)
    {
        float level = value / 255f;
        using (var pill = Theme.RoundedRect(r, r.Width / 2))
        {
            FillVertical(g, pill, Color.FromArgb(0x16, 0x17, 0x1A), Color.FromArgb(0x2A, 0x2B, 0x30));
            var inner = RectangleF.Inflate(r, -2.5f, -2.5f);
            float h = Math.Max(inner.Height * level, level > 0.01f ? inner.Width : 0);
            if (h > 0)
            {
                using var fillPath = Theme.RoundedRect(new RectangleF(inner.X, inner.Y, inner.Width, Math.Min(h, inner.Height)), inner.Width / 2);
                FillVertical(g, fillPath, Color.FromArgb(0xFF, 0x7A, 0x7F), DsMeterRed);
            }
            using var pen = new Pen(Color.FromArgb(0x9A, 0x9C, 0xA2), 1.6f);
            g.DrawPath(pen, pill);
        }
        float[] lengths = { 12, 8.5f, 5 };
        float[] ys = { 31, 38, 45 };
        for (int i = 0; i < 3; i++)
        {
            bool lit = level >= (i + 1) / 3f - 0.01f;
            using var bar = new Pen(lit ? DsMeterRed : Color.FromArgb(0x8E, 0x90, 0x96), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float x0 = barsRight ? r.Right + 4 : r.X - 4;
            g.DrawLine(bar, x0, ys[i], barsRight ? x0 + lengths[i] : x0 - lengths[i], ys[i]);
        }
    }

    static void DsShoulder(Graphics g, bool right, byte l2, bool l1)
    {
        PointF[] Side(PointF[] pts) => right ? Mirror(pts) : pts;
        // Measured: top ~ (68, 9), outer side x 54@18 / 46@30 / 40@40, inner side x 86 from y 15 to 36.
        var blob = Side(new PointF[] { new(62, 10), new(74, 9), new(83, 13), new(87, 23), new(88, 35), new(95, 43), new(97, 50), new(70, 52), new(41, 51), new(38, 44), new(43, 31), new(51, 18) });
        using var path = new GraphicsPath();
        path.AddClosedCurve(blob, 0.35f);

        bool l2Down = l2 > 20;
        if (l1 || l2Down)
            GlowRect(g, path.GetBounds(), 6, Theme.PsBlue, 120);
        DropShadow(g, path, 2.5f, 120, 3);
        // L2/R2: the back, upper part of the cap, lit on top.
        FillVertical(g, path, l2Down ? Color.FromArgb(0x5A, 0x8C, 0xF0) : Color.FromArgb(0x4A, 0x4C, 0x53),
            l2Down ? Color.FromArgb(0x24, 0x52, 0xB8) : Color.FromArgb(0x1E, 0x1F, 0x23));

        // L1/R1: the lower, front part of the cap.
        var state = g.Save();
        g.SetClip(path);
        using (var front = new GraphicsPath())
        {
            front.AddClosedCurve(Side(new PointF[] { new(38, 34), new(60, 26), new(86, 25), new(95, 36), new(98, 54), new(36, 54) }), 0.4f);
            FillVertical(g, front, l1 ? Color.FromArgb(0x7A, 0xA6, 0xFF) : Color.FromArgb(0x30, 0x31, 0x36),
                l1 ? Theme.PsBlue : Color.FromArgb(0x16, 0x17, 0x19));
            // Lit top edge of the bumper.
            using var edge = new Pen(Color.FromArgb(l1 ? 200 : 110, 0xB8, 0xBE, 0xC8), 0.8f);
            g.DrawPath(edge, front);
        }
        g.Restore(state);

        using var font = new Font("Segoe UI Semibold", 4.4f, GraphicsUnit.Pixel);
        var l2At = right ? Mirror(new PointF(77, 18)) : new PointF(77, 18);
        var l1At = right ? Mirror(new PointF(77, 31)) : new PointF(77, 31);
        DrawCentered(g, right ? "R2" : "L2", font, Color.FromArgb(0xC4, 0xC7, 0xCD), l2At);
        DrawCentered(g, right ? "R1" : "L1", font, Color.FromArgb(0xC4, 0xC7, 0xCD), l1At);
    }

    static void DsPill(Graphics g, PointF c, bool pressed, bool create)
    {
        var r = new RectangleF(c.X - 4.5f, c.Y - 7, 9, 14);
        if (pressed)
            GlowRect(g, r, 5, Theme.PsBlue);
        using (var path = Theme.RoundedRect(r, 4.5f))
        {
            DropShadow(g, path, 1.5f, 90, 2);
            using var fill = new LinearGradientBrush(r, pressed ? Theme.PsBlue : Color.FromArgb(0xEC, 0xEF, 0xF3),
                pressed ? ControlPaint.Dark(Theme.PsBlue, 0.1f) : Color.FromArgb(0xC2, 0xC8, 0xD1), LinearGradientMode.Horizontal);
            using var pen = new Pen(Color.FromArgb(0xA8, 0xAF, 0xBA), 0.8f);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        using var ink = new Pen(Color.FromArgb(0x9A, 0x9F, 0xA8), 0.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float top = c.Y - 10;
        if (create)
        {
            g.DrawLine(ink, c.X, top - 3, c.X, top - 0.5f);
            g.DrawLine(ink, c.X - 2.6f, top - 2.2f, c.X - 1.6f, top - 0.4f);
            g.DrawLine(ink, c.X + 2.6f, top - 2.2f, c.X + 1.6f, top - 0.4f);
        }
        else
        {
            for (int i = 0; i < 3; i++)
                g.DrawLine(ink, c.X - 2.4f, top - 3 + i * 1.3f, c.X + 2.4f, top - 3 + i * 1.3f);
        }
    }

    static void DsDpad(Graphics g, PointF c, bool up, bool down, bool left, bool right)
    {
        // Four black keys, each a rounded block that narrows to a point toward the middle,
        // lit from above with a shadow on the shell below.
        void Key(bool pressed, float angle)
        {
            var state = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(angle);
            using var path = new GraphicsPath();
            path.AddClosedCurve(new PointF[] { new(-8.5f, -23), new(8.5f, -23), new(9.5f, -10), new(0, -3.5f), new(-9.5f, -10) }, 0.22f);
            g.Restore(state);

            // Shadow and light are in screen space, so transform the key path first.
            using var m = new Matrix();
            m.Translate(c.X, c.Y);
            m.Rotate(angle);
            path.Transform(m);
            if (pressed)
                Glow(g, Center(path), 15, Theme.PsBlue, 190);
            DropShadow(g, path, 2.2f, 110, 3);
            FillVertical(g, path, pressed ? Color.FromArgb(0x7A, 0xA6, 0xFF) : Color.FromArgb(0x3C, 0x3E, 0x44),
                pressed ? Theme.PsBlue : Color.FromArgb(0x12, 0x13, 0x15));
            using var rim = new Pen(Color.FromArgb(90, 0xFF, 0xFF, 0xFF), 0.6f);
            g.DrawPath(rim, path);

            var arrowState = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(angle);
            using var arrow = new SolidBrush(Color.FromArgb(0xEE, 0xF0, 0xF3));
            g.FillPolygon(arrow, new PointF[] { new(0, -17.5f), new(2.2f, -14.5f), new(-2.2f, -14.5f) });
            g.Restore(arrowState);
        }

        static PointF Center(GraphicsPath p)
        {
            var b = p.GetBounds();
            return new PointF(b.X + b.Width / 2, b.Y + b.Height / 2);
        }

        Key(up, 0);
        Key(right, 90);
        Key(down, 180);
        Key(left, 270);
    }

    static void DsFace(Graphics g, PointF c, PsSymbol symbol, bool pressed, Color color)
    {
        const float r = 10.5f;
        var rect = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
        if (pressed)
            Glow(g, c, r * 2f, color, 120);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(rect);
            DropShadow(g, path, 2.2f, 120, 3);
            // Domed key: lighter toward the top-left.
            using var dome = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(c.X - r * 0.35f, c.Y - r * 0.45f),
                CenterColor = Color.FromArgb(0x48, 0x4A, 0x51),
                SurroundColors = new[] { Color.FromArgb(0x17, 0x18, 0x1B) },
            };
            g.FillPath(dome, path);
        }
        using (var rim = new Pen(pressed ? DsRing : Color.FromArgb(0x0E, 0x0F, 0x11), pressed ? 2f : 1f))
            g.DrawEllipse(rim, rect);

        using var pen = new Pen(pressed ? color : DsSymbol, pressed ? 1.9f : 1.1f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (symbol)
        {
            case PsSymbol.Triangle:
                g.DrawPolygon(pen, new PointF[] { new(c.X, c.Y - 5.6f), new(c.X + 5.9f, c.Y + 4f), new(c.X - 5.9f, c.Y + 4f) });
                break;
            case PsSymbol.Circle:
                g.DrawEllipse(pen, c.X - 5.6f, c.Y - 5.6f, 11.2f, 11.2f);
                break;
            case PsSymbol.Cross:
                g.DrawLine(pen, c.X - 5, c.Y - 5, c.X + 5, c.Y + 5);
                g.DrawLine(pen, c.X + 5, c.Y - 5, c.X - 5, c.Y + 5);
                break;
            case PsSymbol.Square:
                g.DrawRectangle(pen, c.X - 5.2f, c.Y - 4.6f, 10.4f, 9.2f);
                break;
        }
    }

    static void DsStick(Graphics g, PointF c, short x, short y, bool pressed)
    {
        const float cap = 18.5f, travel = 5;
        var k = new PointF(c.X + x / 32768f * travel, c.Y - y / 32768f * travel);
        if (pressed)
            Glow(g, k, cap + 9, Theme.PsBlue, 170);
        var capRect = new RectangleF(k.X - cap, k.Y - cap, cap * 2, cap * 2);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(capRect);
            DropShadow(g, path, 2.5f, 140, 3);
            // Outer skirt of the cap, lit on top.
            FillVertical(g, path, Color.FromArgb(0x4A, 0x4E, 0x64), Color.FromArgb(0x16, 0x18, 0x22));
        }
        // Concave top: darker in the middle, a light ring around it.
        var dish = new RectangleF(k.X - 14, k.Y - 13, 28, 28);
        using (var dishPath = new GraphicsPath())
        {
            dishPath.AddEllipse(dish);
            using var concave = new PathGradientBrush(dishPath)
            {
                CenterPoint = new PointF(k.X, k.Y + 3),
                CenterColor = Color.FromArgb(0x1E, 0x21, 0x2E),
                SurroundColors = new[] { Color.FromArgb(0x34, 0x38, 0x4C) },
            };
            g.FillPath(concave, dishPath);
        }
        using (var edge = new Pen(pressed ? DsRing : Color.FromArgb(0x0C, 0x0D, 0x12), pressed ? 2f : 1f))
            g.DrawEllipse(edge, capRect);
        using (var ring = new Pen(pressed ? Color.FromArgb(0xBF, 0xD6, 0xFF) : Color.FromArgb(0x9A, 0x9E, 0xB4), 0.8f))
            g.DrawEllipse(ring, dish);
    }

    enum PsSymbol
    {
        Triangle,
        Circle,
        Cross,
        Square,
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
