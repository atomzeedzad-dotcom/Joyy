using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using WpfPixelFormats = System.Windows.Media.PixelFormats;

namespace Joyy.App;

/// <summary>
/// A user-supplied product photo of a controller, with its plain background removed and the
/// picture cropped to the controller. Button positions are stored as fractions of the original
/// photo, so the same photo at another resolution still lines up.
/// </summary>
internal sealed class PhotoSkin : IDisposable
{
    static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    PhotoSkin(Bitmap image, Rectangle crop, Size source)
    {
        Image = image;
        Crop = crop;
        Source = source;
    }

    /// <summary>The controller with a transparent background, cropped.</summary>
    public Bitmap Image { get; }

    /// <summary>Where <see cref="Image"/> sits inside the original photo, in source pixels.</summary>
    public Rectangle Crop { get; }

    public Size Source { get; }

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Joyy", "photos");

    static string BaseName(PadView.PadKind kind) => kind == PadView.PadKind.DualSense ? "dualsense" : "xbox";

    /// <summary>The installed photo for this controller, or null to use the built-in drawing.</summary>
    public static string? Find(PadView.PadKind kind)
    {
        foreach (var ext in Extensions)
        {
            string path = Path.Combine(Folder, BaseName(kind) + ext);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>Copies a photo into Joyy's folder (replacing any previous one) and loads it.</summary>
    public static PhotoSkin Install(PadView.PadKind kind, string sourcePath)
    {
        var skin = Load(sourcePath); // validate before replacing anything
        Directory.CreateDirectory(Folder);
        Remove(kind);
        File.Copy(sourcePath, Path.Combine(Folder, BaseName(kind) + Path.GetExtension(sourcePath).ToLowerInvariant()));
        return skin;
    }

    /// <summary>Deletes Joyy's copy of the photo so the built-in drawing is used again.</summary>
    public static void Remove(PadView.PadKind kind)
    {
        foreach (var ext in Extensions)
            File.Delete(Path.Combine(Folder, BaseName(kind) + ext));
    }

    public static PhotoSkin Load(string path)
    {
        // WIC via WPF decodes PNG, JPEG and WebP alike.
        BitmapSource frame;
        using (var stream = File.OpenRead(path))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            frame = new FormatConvertedBitmap(decoder.Frames[0], WpfPixelFormats.Bgra32, null, 0);
        }
        int w = frame.PixelWidth, h = frame.PixelHeight;
        var pixels = new byte[w * h * 4];
        frame.CopyPixels(pixels, w * 4, 0);

        var crop = RemoveBackground(pixels, w, h);

        var bitmap = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, crop.Width, crop.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < crop.Height; y++)
            Marshal.Copy(pixels, ((crop.Y + y) * w + crop.X) * 4, data.Scan0 + y * data.Stride, crop.Width * 4);
        bitmap.UnlockBits(data);

        return new PhotoSkin(bitmap, crop, new Size(w, h));
    }

    /// <summary>
    /// Flood-fills the plain backdrop from the photo's edges (so same-colored parts inside the
    /// controller, like a white logo or the blue light bar, are kept), feathers the boundary and
    /// returns the bounding box of what is left.
    /// </summary>
    static Rectangle RemoveBackground(byte[] px, int w, int h)
    {
        const int Solid = 60, Feather = 105; // color distance: below = backdrop, between = soft edge

        var bg = BackdropColor(px, w, h);
        int Distance(int i)
        {
            int db = px[i] - bg.B, dg = px[i + 1] - bg.G, dr = px[i + 2] - bg.R;
            return (int)Math.Sqrt(db * db + dg * dg + dr * dr);
        }

        var visited = new bool[w * h];
        var queue = new Queue<int>();
        void Seed(int x, int y)
        {
            int p = y * w + x;
            if (!visited[p] && Distance(p * 4) < Solid)
            {
                visited[p] = true;
                queue.Enqueue(p);
            }
        }
        for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }

        void Visit(int q)
        {
            if (visited[q])
                return;
            visited[q] = true;
            int d = Distance(q * 4);
            if (d < Solid)
            {
                queue.Enqueue(q);
            }
            else if (d < Feather)
            {
                // Edge pixel: partly transparent, with the backdrop color taken back out.
                float a = (d - Solid) / (float)(Feather - Solid);
                int j = q * 4;
                px[j] = Unmix(px[j], bg.B, a);
                px[j + 1] = Unmix(px[j + 1], bg.G, a);
                px[j + 2] = Unmix(px[j + 2], bg.R, a);
                px[j + 3] = (byte)(a * 255);
            }
        }

        while (queue.Count > 0)
        {
            int p = queue.Dequeue();
            px[p * 4 + 3] = 0;
            int x = p % w, y = p / w;
            if (x > 0) Visit(p - 1);
            if (x < w - 1) Visit(p + 1);
            if (y > 0) Visit(p - w);
            if (y < h - 1) Visit(p + w);
        }

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (px[(y * w + x) * 4 + 3] < 40)
                    continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0)
            throw new InvalidOperationException("The photo seems to be empty after removing its background.");
        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    static byte Unmix(byte value, byte background, float alpha) =>
        (byte)Math.Clamp((value - background * (1 - alpha)) / Math.Max(alpha, 0.05f), 0, 255);

    /// <summary>The most common (coarsely quantized) color along the photo's border.</summary>
    static (byte R, byte G, byte B) BackdropColor(byte[] px, int w, int h)
    {
        var counts = new Dictionary<int, (int Count, long R, long G, long B)>();
        void Add(int x, int y)
        {
            int i = (y * w + x) * 4;
            int key = (px[i + 2] >> 4) << 8 | (px[i + 1] >> 4) << 4 | (px[i] >> 4);
            counts.TryGetValue(key, out var c);
            counts[key] = (c.Count + 1, c.R + px[i + 2], c.G + px[i + 1], c.B + px[i]);
        }
        for (int x = 0; x < w; x += 2) { Add(x, 0); Add(x, h - 1); }
        for (int y = 0; y < h; y += 2) { Add(0, y); Add(w - 1, y); }
        var best = counts.Values.MaxBy(c => c.Count);
        return ((byte)(best.R / best.Count), (byte)(best.G / best.Count), (byte)(best.B / best.Count));
    }

    public void Dispose() => Image.Dispose();
}
