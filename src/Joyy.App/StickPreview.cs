using System.Drawing.Drawing2D;
using Joyy.Core;

namespace Joyy.App;

/// <summary>
/// Live stick circle like DSX's "Stick adjustment preview": the deadzone and full-tilt rings,
/// the raw controller input (white) and what the game receives after the profile (accent).
/// </summary>
internal sealed class StickPreview : Control
{
    float _rawX, _rawY, _outX, _outY;
    double _deadzone = 0.08, _saturation = 1.0;

    public StickPreview(string caption)
    {
        Text = caption;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Size = new Size(200, 210);
    }

    public void SetZones(double deadzone, double saturation)
    {
        _deadzone = deadzone;
        _saturation = saturation;
        Invalidate();
    }

    public void SetInput(short rawX, short rawY, short outX, short outY)
    {
        float rx = rawX / 32768f, ry = rawY / 32768f, ox = outX / 32768f, oy = outY / 32768f;
        if (rx == _rawX && ry == _rawY && ox == _outX && oy == _outY)
            return;
        (_rawX, _rawY, _outX, _outY) = (rx, ry, ox, oy);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        int captionH = LogicalToDeviceUnits(22);
        float size = Math.Min(Width, Height - captionH) - LogicalToDeviceUnits(10);
        float r = size / 2;
        var c = new PointF(Width / 2f, captionH + (Height - captionH) / 2f);

        using (var face = new SolidBrush(Theme.Raised))
            g.FillEllipse(face, c.X - r, c.Y - r, size, size);
        using (var axis = new Pen(Color.FromArgb(50, Theme.Text)))
        {
            g.DrawLine(axis, c.X - r, c.Y, c.X + r, c.Y);
            g.DrawLine(axis, c.X, c.Y - r, c.X, c.Y + r);
        }

        float dz = (float)_deadzone * r;
        using (var dzBrush = new SolidBrush(Color.FromArgb(70, Theme.Bad)))
            g.FillEllipse(dzBrush, c.X - dz, c.Y - dz, dz * 2, dz * 2);
        float sat = (float)_saturation * r;
        using (var satPen = new Pen(Color.FromArgb(150, Theme.Good), 1.5f) { DashStyle = DashStyle.Dash })
            g.DrawEllipse(satPen, c.X - sat, c.Y - sat, sat * 2, sat * 2);
        using (var rim = new Pen(Theme.Border, 1.5f))
            g.DrawEllipse(rim, c.X - r, c.Y - r, size, size);

        // Screen Y grows downward; stick Y is up-positive.
        var raw = new PointF(c.X + _rawX * r, c.Y - _rawY * r);
        var output = new PointF(c.X + _outX * r, c.Y - _outY * r);
        float d = LogicalToDeviceUnits(5);
        using (var rawBrush = new SolidBrush(Color.White))
            g.FillEllipse(rawBrush, raw.X - d, raw.Y - d, d * 2, d * 2);
        using (var outBrush = new SolidBrush(Theme.Accent))
        using (var outRim = new Pen(Color.White, 1.2f))
        {
            g.FillEllipse(outBrush, output.X - d, output.Y - d, d * 2, d * 2);
            g.DrawEllipse(outRim, output.X - d, output.Y - d, d * 2, d * 2);
        }

        using var font = new Font("Segoe UI Semibold", 9f);
        TextRenderer.DrawText(g, Text, font, new Rectangle(0, 0, Width, captionH), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
