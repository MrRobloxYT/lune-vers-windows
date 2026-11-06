using System.Runtime.InteropServices;
using LuneVers;

namespace Lune;

/// "Follow screen colors": samples what's on screen just inside each edge
/// (past the light itself, so it never sees its own glow) a few times a
/// second, giving one color for the top, right, bottom and left.
public sealed class ScreenColors
{
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr hdc, int x, int y);

    readonly object gate = new();
    List<Rgb>? colors;
    CancellationTokenSource? cts;

    public List<Rgb>? Colors { get { lock (gate) return colors; } }

    public void Run(bool on, Func<float> inset)
    {
        if (on == (cts is not null)) return;
        if (!on) { cts?.Cancel(); cts = null; lock (gate) colors = null; return; }
        var c = cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!c.IsCancellationRequested)
            {
                try { var sample = Sample(inset()); lock (gate) colors = sample; } catch { }
                await Task.Delay(400);
            }
        });
    }

    static List<Rgb>? Sample(float insetPoints)
    {
        var m = Native.Monitors().FirstOrDefault();
        if (m is null) return null;
        int inset = (int)(insetPoints * m.Scale) + 12;
        var b = m.Bounds;
        if (b.Width < inset * 3 || b.Height < inset * 3) return null;
        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            Rgb Avg(Func<int, (int X, int Y)> at)
            {
                float r = 0, g = 0, bl = 0; const int n = 14;
                for (int i = 0; i < n; i++)
                {
                    var (x, y) = at(i);
                    uint px = GetPixel(dc, x, y);
                    r += (px & 0xFF) / 255f; g += ((px >> 8) & 0xFF) / 255f; bl += ((px >> 16) & 0xFF) / 255f;
                }
                return Palette.Vivid(new Rgb(r / n, g / n, bl / n));
            }
            int w = b.Width, h = b.Height;
            return new List<Rgb>
            {
                Avg(i => (b.Left + w * (i + 1) / 15, b.Top + inset)),
                Avg(i => (b.Right - inset, b.Top + h * (i + 1) / 15)),
                Avg(i => (b.Right - w * (i + 1) / 15, b.Bottom - inset)),
                Avg(i => (b.Left + inset, b.Bottom - h * (i + 1) / 15)),
            };
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }
}
