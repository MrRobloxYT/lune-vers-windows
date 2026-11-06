using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LuneVers;

public readonly record struct Rgb(float R, float G, float B)
{
    public static Rgb operator +(Rgb a, Rgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
    public static Rgb operator -(Rgb a, Rgb b) => new(a.R - b.R, a.G - b.G, a.B - b.B);
    public static Rgb operator *(Rgb a, float k) => new(a.R * k, a.G * k, a.B * k);
    public static Rgb operator /(Rgb a, float k) => new(a.R / k, a.G / k, a.B / k);
    public float DistanceSquared(Rgb o) { float r = R - o.R, g = G - o.G, b = B - o.B; return r * r + g * g + b * b; }
    public Rgb Lerp(Rgb to, float k) => this + (to - this) * k;

    static byte Byte(float v) => (byte)Math.Clamp(MathF.Round(v * 255), 0, 255);
    public Color Color => Color.FromRgb(Byte(R), Byte(G), Byte(B));
    public Color WithAlpha(double a) => Color.FromArgb((byte)Math.Clamp(Math.Round(a * 255), 0, 255), Byte(R), Byte(G), Byte(B));
    public static Rgb From(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
    public string Hex => $"#{Byte(R):X2}{Byte(G):X2}{Byte(B):X2}";
    public static Rgb FromHex(string hex)
    {
        try { var c = (Color)ColorConverter.ConvertFromString(hex); return From(c); } catch { return new Rgb(1, 0.2f, 0.4f); }
    }

    /// Hue, saturation and brightness, each 0…1.
    public (float H, float S, float V) Hsb()
    {
        float max = MathF.Max(R, MathF.Max(G, B)), min = MathF.Min(R, MathF.Min(G, B)), d = max - min;
        float h = 0;
        if (d > 1e-6f)
        {
            if (max == R) h = ((G - B) / d) % 6;
            else if (max == G) h = (B - R) / d + 2;
            else h = (R - G) / d + 4;
            h /= 6;
            if (h < 0) h += 1;
        }
        return (h, max > 0 ? d / max : 0, max);
    }

    public static Rgb FromHsb(float h, float s, float v)
    {
        h = ((h % 1) + 1) % 1;
        float c = v * s, x = c * (1 - MathF.Abs(h * 6 % 2 - 1)), m = v - c;
        (float r, float g, float b) = (int)(h * 6) switch
        {
            0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x), 3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x),
        };
        return new Rgb(r + m, g + m, b + m);
    }
}

/// Finds the most prominent vivid colors in album art with a small k-means,
/// the same way the Mac apps do.
public static class Palette
{
    /// Decodes cover art for display (frozen, so any thread can use it).
    public static BitmapSource? Decode(byte[] bytes, int width = 0)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            if (width > 0) image.DecodePixelWidth = width;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    public static List<Rgb>? FromImage(BitmapSource image, int count = 3)
    {
        const int w = 48, h = 48;
        var scaled = new TransformedBitmap(image, new ScaleTransform((double)w / image.PixelWidth, (double)h / image.PixelHeight));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        int pw = bgra.PixelWidth, ph = bgra.PixelHeight;
        var data = new byte[pw * ph * 4];
        bgra.CopyPixels(data, pw * 4, 0);
        return FromPixels(data, count);
    }

    /// `bgra` is 8-bit BGRA pixels.
    public static List<Rgb>? FromPixels(byte[] bgra, int count = 3)
    {
        var colorful = new List<(Rgb C, float W)>();
        var all = new List<(Rgb C, float W)>();
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            var c = new Rgb(bgra[i + 2] / 255f, bgra[i + 1] / 255f, bgra[i] / 255f);
            float mx = MathF.Max(c.R, MathF.Max(c.G, c.B)), mn = MathF.Min(c.R, MathF.Min(c.G, c.B));
            float sat = mx > 0 ? (mx - mn) / mx : 0;
            all.Add((c, 0.2f + mx));
            // Coverage counts most; saturation breaks ties toward real color.
            if (sat > 0.22f && mx > 0.2f) colorful.Add((c, 0.6f + sat));
        }
        var source = colorful.Count > bgra.Length / 4 * 0.06f ? colorful : all;
        var clusters = KMeans(source, Math.Max(1, count) + 2);
        if (clusters is null) return null;
        // Merge clusters whose hues are nearly the same.
        var picked = new List<(Rgb C, float W)>();
        foreach (var (c, wgt) in clusters)
        {
            var hc = c.Hsb();
            int j = picked.FindIndex(p => { var hp = p.C.Hsb(); float dh = MathF.Abs(hp.H - hc.H); return MathF.Min(dh, 1 - dh) < 0.055f && MathF.Abs(hp.S - hc.S) < 0.35f; });
            if (j >= 0) picked[j] = (picked[j].C, picked[j].W + wgt); else picked.Add((c, wgt));
        }
        var output = picked.OrderByDescending(p => p.W).Select(p => Vivid(p.C)).ToList();
        // Not enough distinct colors: add a deeper shade of the main one.
        while (output.Count < count && output.Count > 0)
        {
            var hc = output[0].Hsb();
            output.Add(Rgb.FromHsb((hc.H + 0.03f * output.Count) % 1, MathF.Min(1, hc.S + 0.15f), 0.72f));
        }
        return output.Take(Math.Max(1, count)).ToList();
    }

    static List<(Rgb C, float W)>? KMeans(List<(Rgb C, float W)> pixels, int k)
    {
        if (pixels.Count == 0) return null;
        var sorted = pixels.OrderByDescending(p => p.W).ToList();
        var centers = Enumerable.Range(0, k).Select(i => sorted[Math.Min(sorted.Count - 1, i * sorted.Count / Math.Max(1, k))].C).ToArray();
        var weights = new float[k];
        for (int iter = 0; iter < 12; iter++)
        {
            var sums = new Rgb[k];
            weights = new float[k];
            foreach (var (c, w) in pixels)
            {
                int best = 0; float bestD = float.MaxValue;
                for (int j = 0; j < k; j++) { float d = c.DistanceSquared(centers[j]); if (d < bestD) { bestD = d; best = j; } }
                sums[best] += c * w; weights[best] += w;
            }
            for (int j = 0; j < k; j++) if (weights[j] > 0) centers[j] = sums[j] / weights[j];
        }
        return centers.Zip(weights).Where(p => p.Second > 0).OrderByDescending(p => p.Second).Select(p => (p.First, p.Second)).ToList();
    }

    /// Keeps the album's hue but makes it bright enough to read as light.
    public static Rgb Vivid(Rgb c)
    {
        var h = c.Hsb();
        float sat = h.S < 0.12f ? h.S : MathF.Min(1, MathF.Max(0.5f, h.S * 1.08f));
        return Rgb.FromHsb(h.H, sat, MathF.Max(0.85f, h.V));
    }
}
