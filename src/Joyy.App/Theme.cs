using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Joyy.App;

/// <summary>Dark theme: palette plus helpers that make stock WinForms controls fit it.</summary>
internal static partial class Theme
{
    // Window background: a purple-to-navy diagonal gradient; content sits on rounded cards.
    public static readonly Color BgTop = Color.FromArgb(0x2A, 0x1C, 0x3D);
    public static readonly Color BgBottom = Color.FromArgb(0x10, 0x16, 0x2A);
    public static readonly Color Background = Color.FromArgb(0x1A, 0x18, 0x2E);
    public static readonly Color Card = Color.FromArgb(0x26, 0x20, 0x38);
    public static readonly Color Raised = Color.FromArgb(0x31, 0x29, 0x46);
    public static readonly Color Hover = Color.FromArgb(0x38, 0x2F, 0x50);
    public static readonly Color Border = Color.FromArgb(0x3D, 0x34, 0x55);
    public static readonly Color Text = Color.FromArgb(0xEE, 0xEA, 0xF5);
    public static readonly Color Muted = Color.FromArgb(0xA6, 0x9C, 0xBA);
    public static readonly Color Accent = Color.FromArgb(0xE2, 0x6B, 0xE0);   // pink-violet, buttons/toggles/selection
    public static readonly Color PsBlue = Color.FromArgb(0x4C, 0x8D, 0xFF);   // DualSense highlights
    public static readonly Color Good = Color.FromArgb(0x3D, 0xD6, 0x8C);
    public static readonly Color Warn = Color.FromArgb(0xF5, 0xB1, 0x4C);
    public static readonly Color Bad = Color.FromArgb(0xFF, 0x6B, 0x6B);
    public static readonly Color Idle = Color.FromArgb(0x7D, 0x74, 0x92);

    /// <summary>Windows 11 icon font, falling back to the Windows 10 one.</summary>
    public static readonly FontFamily IconFamily = PickIconFamily();

    static FontFamily PickIconFamily()
    {
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            try { return new FontFamily(name); }
            catch (ArgumentException) { }
        }
        return FontFamily.GenericSansSerif;
    }

    public static Font IconFont(float points) => new(IconFamily, points);

    /// <summary>Glyphs from Segoe Fluent Icons / MDL2 Assets.</summary>
    public static class Glyph
    {
        public const string Home = "";
        public const string Controller = "";
        public const string Profiles = "";
        public const string Mapping = "";
        public const string Sticks = "";
        public const string Hide = "";
        public const string Log = "";
        public const string Settings = "";
        public const string Info = "";
        public const string Chevron = "";
        public const string Back = "";
        public const string Check = "";
        public const string Cancel = "";
        public const string Warning = "";
        public const string Power = "";
        public const string Bluetooth = "";
        public const string Usb = "";
        public const string Add = "";
        public const string Delete = "";
        public const string Folder = "";
        public const string Vibrate = "";
        public const string Picture = "";
        public const string Refresh = "";
        public const string Shield = "";
        public const string Link = "";
        public const string Game = "";
        public const string Edit = "";
    }

    public static void PaintBackground(Graphics g, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        using var brush = new LinearGradientBrush(bounds, BgTop, BgBottom, 35f);
        g.FillRectangle(brush, bounds);
    }

    // PlayStation symbol colors (used for the press glow) and Xbox 360 face buttons.
    public static readonly Color PsTriangle = Color.FromArgb(0x2B, 0xD4, 0x7D);
    public static readonly Color PsCircle = Color.FromArgb(0xFF, 0x4D, 0x5A);
    public static readonly Color PsCross = Color.FromArgb(0x5B, 0x8C, 0xFF);
    public static readonly Color PsSquare = Color.FromArgb(0xFF, 0x6F, 0xD8);
    public static readonly Color XbA = Color.FromArgb(0x3D, 0xBE, 0x4E);
    public static readonly Color XbB = Color.FromArgb(0xE0, 0x4A, 0x3B);
    public static readonly Color XbX = Color.FromArgb(0x2F, 0x7B, 0xD6);
    public static readonly Color XbY = Color.FromArgb(0xF2, 0xC2, 0x30);
    public static readonly Color XbGreen = Color.FromArgb(0x7C, 0xDB, 0x5A);

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ---- control styling ----

    /// <summary>Dark title bar (Windows 10 2004+ / 11).</summary>
    public static void DarkTitleBar(Form form)
    {
        int on = 1;
        if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int)); // pre-20H1 attribute id
    }

    /// <summary>Dark scrollbars and inner chrome for list-like controls.</summary>
    public static void DarkScrollbars(Control control)
    {
        if (control.IsHandleCreated)
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        else
            control.HandleCreated += (_, _) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
    }

    public static Button MakeButton(string text, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Raised,
            ForeColor = primary ? Color.White : Text,
            Padding = new Padding(10, 3, 10, 3),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = primary ? Accent : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(Accent, 0.15f) : Hover;
        b.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(Accent, 0.05f) : Border;
        b.EnabledChanged += (_, _) => b.ForeColor = b.Enabled ? (primary ? Color.White : Text) : Muted;
        return b;
    }

    public static CheckBox MakeCheckBox(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Text,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(3, 4, 12, 2),
        Cursor = Cursors.Hand,
    };

    public static void Input(Control c)
    {
        c.BackColor = Raised;
        c.ForeColor = Text;
        switch (c)
        {
            case NumericUpDown n:
                n.BorderStyle = BorderStyle.FixedSingle;
                break;
            case TextBox t:
                t.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat;
                break;
        }
        DarkScrollbars(c);
    }

    public static Label Heading(string text, float size = 10.5f) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Text,
        Font = new Font("Segoe UI Semibold", size),
    };

    public static Label Caption(string text) => new() { Text = text, AutoSize = true, ForeColor = Muted };

    /// <summary>Dark tray/context menus.</summary>
    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new Colors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = new Rectangle(e.ImageRectangle.X, e.ImageRectangle.Y, e.ImageRectangle.Width, e.ImageRectangle.Height);
            using var pen = new Pen(Accent, 2f);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.52f),
                new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.72f),
                new PointF(r.X + r.Width * 0.78f, r.Y + r.Height * 0.30f),
            });
        }

        sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Card;
            public override Color ImageMarginGradientBegin => Card;
            public override Color ImageMarginGradientMiddle => Card;
            public override Color ImageMarginGradientEnd => Card;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => Hover;
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Hover;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Border;
            public override Color CheckBackground => Card;
            public override Color CheckSelectedBackground => Hover;
            public override Color CheckPressedBackground => Hover;
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetWindowTheme(nint hwnd, string? appName, string? idList);
}

/// <summary>Tray and window icons drawn at startup: a small gamepad in the status color.</summary>
internal static partial class Icons
{
    public static readonly Icon Ready = Create(Theme.Good);
    public static readonly Icon Waiting = Create(Theme.Idle);
    public static readonly Icon Error = Create(Theme.Bad);
    public static readonly Icon App = FromExe() ?? Create(Theme.Accent);

    /// <summary>The icon embedded in Joyy.exe (same one Start menu and the taskbar show).</summary>
    static Icon? FromExe()
    {
        try
        {
            return Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static Icon Create(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(color);
            using (var path = Theme.RoundedRect(new RectangleF(2, 7, 28, 15), 7))
                g.FillPath(body, path);
            g.FillEllipse(body, 1, 12, 13, 16);
            g.FillEllipse(body, 18, 12, 13, 16);

            using var white = new SolidBrush(Color.White);
            g.FillRectangle(white, 6, 13, 7, 2);   // d-pad
            g.FillRectangle(white, 8.5f, 10.5f, 2, 7);
            g.FillEllipse(white, 20, 10, 3.5f, 3.5f); // face buttons
            g.FillEllipse(white, 23.5f, 13.5f, 3.5f, 3.5f);
        }

        nint handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);
}
