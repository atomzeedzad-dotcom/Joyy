using Joyy.Core;

namespace Joyy.App;

/// <summary>
/// "Joyy.exe --render-preview &lt;folder&gt;": renders the controller pictures (at rest and with
/// sample buttons held) to PNG files, for documentation and for checking the artwork without
/// a controller in hand.
/// </summary>
internal static class PreviewRenderer
{
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var mapper = new Mapper(new Profile());

        var held = new PadState(
            PhysicalButtons.Of(PhysicalButton.Cross, PhysicalButton.Triangle, PhysicalButton.R1,
                PhysicalButton.DpadUp, PhysicalButton.PS, PhysicalButton.L3, PhysicalButton.Touchpad),
            LeftX: 20000, LeftY: 22000, RightX: -26000, RightY: 0, L2: 200, R2: 90);

        // Every button at once: shows whether each highlight sits on its button in a photo.
        uint all = 0;
        foreach (var b in Enum.GetValues<PhysicalButton>())
            all |= PhysicalButtons.Bit(b);
        var everything = new PadState(all, 0, 0, 0, 0, 255, 255);

        Render(Path.Combine(folder, "pads-rest.png"), PadState.Neutral, mapper.Map(PadState.Neutral));
        Render(Path.Combine(folder, "pads-pressed.png"), held, mapper.Map(held));
        Render(Path.Combine(folder, "pads-all.png"), everything, mapper.Map(everything));
        RenderWindow(Path.Combine(folder, "window.png"));
        return 0;
    }

    /// <summary>The whole main window, drawn offscreen (no bridge running).</summary>
    static void RenderWindow(string path)
    {
        using var form = new MainForm(new NoActions())
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ShowInTaskbar = false,
        };
        _ = form.Handle; // same order as TrayApp: handle first, shown later
        Application.DoEvents();
        form.Show();
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        form.CloseForReal();
    }

    sealed class NoActions : IAppActions
    {
        public void TogglePause() { }
        public string? SaveProfiles(Profile global, IReadOnlyList<Profile> games) => null;
        public void SetStartWithWindows(bool enabled) { }
        public void SetStartMinimized(bool enabled) { }
        public void SetHideDualSense(bool enabled) { }
        public void OpenLogFolder() { }
        public void OpenConfigFile() { }
        public void Exit() { }
    }

    static void Render(string path, PadState physical, X360Report xbox)
    {
        const int w = 560, h = 380;
        using var bitmap = new Bitmap(w * 2 + 24, h);
        using (var g = Graphics.FromImage(bitmap))
            g.Clear(Theme.Card);

        foreach (var (kind, x) in new[] { (PadView.PadKind.DualSense, 0), (PadView.PadKind.Xbox, w + 24) })
        {
            using var view = new PadView(kind) { Size = new Size(w, h) };
            using var skin = PhotoSkin.Find(kind) is { } photo ? PhotoSkin.Load(photo) : null;
            view.Skin = skin;
            view.SetState(physical, xbox, active: true);
            using var one = new Bitmap(w, h);
            view.DrawToBitmap(one, new Rectangle(0, 0, w, h));
            using var g = Graphics.FromImage(bitmap);
            g.DrawImage(one, x, 0);
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
