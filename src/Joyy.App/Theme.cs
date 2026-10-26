using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Joyy.App;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(0xF4, 0xF6, 0xFA);
    public static readonly Color Card = Color.White;
    public static readonly Color Text = Color.FromArgb(0x1D, 0x23, 0x30);
    public static readonly Color Muted = Color.FromArgb(0x6B, 0x73, 0x85);
    public static readonly Color Border = Color.FromArgb(0xDD, 0xE2, 0xEC);
    public static readonly Color Accent = Color.FromArgb(0x3B, 0x6C, 0xF6);
    public static readonly Color Good = Color.FromArgb(0x1F, 0x9D, 0x55);
    public static readonly Color Warn = Color.FromArgb(0xC9, 0x8A, 0x00);
    public static readonly Color Bad = Color.FromArgb(0xD6, 0x45, 0x45);
    public static readonly Color Idle = Color.FromArgb(0x8A, 0x93, 0xA6);

    public static readonly Color PadBody = Color.FromArgb(0xE6, 0xEA, 0xF2);
    public static readonly Color PadShadow = Color.FromArgb(0xCF, 0xD5, 0xE1);
    public static readonly Color Knob = Color.FromArgb(0x3A, 0x41, 0x52);

    // PlayStation face buttons
    public static readonly Color PsTriangle = Color.FromArgb(0x2B, 0xB6, 0x73);
    public static readonly Color PsCircle = Color.FromArgb(0xE5, 0x48, 0x4D);
    public static readonly Color PsCross = Color.FromArgb(0x4C, 0x7D, 0xF0);
    public static readonly Color PsSquare = Color.FromArgb(0xD4, 0x6F, 0xC2);

    // Xbox face buttons
    public static readonly Color XbA = Color.FromArgb(0x2B, 0xA8, 0x4A);
    public static readonly Color XbB = Color.FromArgb(0xE5, 0x48, 0x4D);
    public static readonly Color XbX = Color.FromArgb(0x2F, 0x7F, 0xE0);
    public static readonly Color XbY = Color.FromArgb(0xE0, 0xA8, 0x00);

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Tray and window icons drawn at startup: a small gamepad in the status color.</summary>
internal static partial class Icons
{
    public static readonly Icon Ready = Create(Theme.Good);
    public static readonly Icon Waiting = Create(Theme.Idle);
    public static readonly Icon Error = Create(Theme.Bad);
    public static readonly Icon App = Create(Theme.Accent);

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
