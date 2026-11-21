using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LuneVers;

namespace Lune;

/// Animation state. Phases are integrated from frame time, so speed changes
/// (on a beat, say) never make the light jump. Same tuning as the Mac app.
public sealed class GlowAnimator
{
    public float ColorPhase, WavePhase, Breath, Beat, FlowBoost, Energy;
    /// Song sections: above 0 in a chorus or drop, below 0 in a quiet part.
    public float Drive;
    // Lumn's stop-and-go: a predicted beat clock, pulled onto the song's beats.
    float onsetAge = 10, beatClock, beatInterval = 0.388f;
    bool beatHigh;
    readonly List<float> beatGaps = new();
    public Rgb[] Colors = { new(1, 0.2f, 0.4f) };
    public float[] Weights = { 1 };
    /// Screen colors stay on the edge they came from: no flow, each edge's share.
    public float[]? EdgeWeights;

    static (float Color, float Wave) Tuning(Motion m) => m switch
    {
        Motion.Default => (0.22f, 1.0f),
        Motion.Idle => (0.08f, 0.25f),
        Motion.MusicSync => (0.465f, 0.9f),
        _ => (0f, 0f),
    };

    // Lumn's surge on each beat, speed as a multiple of the glide, every 0.04 s
    // from 0.10 s before the beat (measured from its demo).
    static readonly float[] Curve = { 1, 1.64f, 2.94f, 3.85f, 4.0f, 2.35f, 1.19f, 1, 1 };

    public void Step(float dt, Motion mode, float energyTarget, float beatTarget, IReadOnlyList<Rgb> palette, bool snap,
                     float period = 0, float confidence = 0, double beatTime = 0, double now = 0, float section = 0)
    {
        var t = Tuning(mode);
        float bt = beatTarget > Beat ? 0.04f : 0.22f;
        Beat += (beatTarget - Beat) * (1 - MathF.Exp(-dt / bt));
        float et = energyTarget > Energy ? 0.06f : 0.3f;
        Energy += (energyTarget - Energy) * (1 - MathF.Exp(-dt / et));
        Drive += ((mode == Motion.MusicSync ? section : 0) - Drive) * (1 - MathF.Exp(-dt / 0.8f));
        float music = mode == Motion.MusicSync ? 1 : 0;
        if (mode == Motion.MusicSync)
        {
            onsetAge += dt;
            beatClock += dt;
            if (beatClock >= beatInterval) beatClock -= beatInterval;
            bool rising = beatTarget > 0.5f && !beatHigh;
            beatHigh = beatTarget > 0.4f;
            // When the tempo tracker is sure, follow its beat length and positions, eased in.
            bool locked = confidence > 0.3f && period > 0.2f && beatTime > 0;
            if (locked)
            {
                float p = period;
                while (p > 0.75f) p /= 2;
                while (p < 0.333f) p *= 2;
                beatInterval += (p - beatInterval) * MathF.Min(1, dt * 3);
                float since = (float)(now - beatTime);
                float want = since - MathF.Floor(since / beatInterval) * beatInterval;
                float error = want - beatClock;
                if (error > beatInterval / 2) error -= beatInterval;
                if (error < -beatInterval / 2) error += beatInterval;
                beatClock += error * MathF.Min(1, dt * 4);
                if (beatClock < 0) beatClock += beatInterval;
                if (beatClock >= beatInterval) beatClock -= beatInterval;
            }
            if (!locked && rising && onsetAge > MathF.Max(0.25f, beatInterval * 0.7f))
            {
                if (onsetAge < 1.5f)
                {
                    beatGaps.Add(onsetAge); if (beatGaps.Count > 8) beatGaps.RemoveAt(0);
                    var sorted = beatGaps.OrderBy(g => g).ToList();
                    float gap = sorted[sorted.Count / 2];
                    while (gap > 0.75f) gap /= 2;
                    while (gap < 0.333f) gap *= 2;
                    beatInterval = Math.Clamp(gap, 0.333f, 0.75f);
                }
                onsetAge = 0;
                float err = beatClock < beatInterval / 2 ? beatClock : beatClock - beatInterval;
                beatClock -= err * 0.6f;
                if (beatClock < 0) beatClock += beatInterval;
            }
            float tau = beatClock < beatInterval / 2 ? beatClock : beatClock - beatInterval;
            float pos = (tau + 0.10f) / 0.04f;
            int k = (int)MathF.Floor(pos);
            float C(int i) => Curve[Math.Clamp(i, 0, Curve.Length - 1)];
            float f = pos - k;
            float shape = (pos < 0 || k >= Curve.Length - 1) ? 1 : MathF.Max(1, 0.5f * ((2 * C(k)) + (-C(k - 1) + C(k + 1)) * f
                + (2 * C(k - 1) - 5 * C(k) + 4 * C(k + 1) - C(k + 2)) * f * f
                + (-C(k - 1) + 3 * C(k) - 3 * C(k + 1) + C(k + 2)) * f * f * f));
            float glide = 0.18f / (beatInterval + 0.408f);      // ~0.18 lap per beat, like Lumn
            ColorPhase += dt * glide * (1 + (shape - 1) * (1 + 0.3f * Drive));
        }
        else ColorPhase += dt * t.Color;
        FlowBoost += (music * (Energy * 0.35f + Beat * 0.25f) - FlowBoost) * (1 - MathF.Exp(-dt / 0.6f));
        WavePhase += dt * (t.Wave + FlowBoost);
        Breath += dt * (mode == Motion.Idle ? 0.55f : 0);
        ColorPhase %= 1000; WavePhase %= 6283.185f; Breath %= 6283.185f;
        // Fade between palettes instead of cutting.
        var target = palette.Count == 0 ? new[] { new Rgb(1, 0.2f, 0.4f) } : palette.ToArray();
        if (snap || Colors.Length != target.Length) Colors = target;
        else
        {
            float kk = 1 - MathF.Exp(-dt / 0.45f);
            for (int i = 0; i < Colors.Length; i++) Colors[i] = Colors[i].Lerp(target[i], kk);
        }
        if (EdgeWeights is { } ew && Colors.Length == 4) { Weights = ew; ColorPhase = 0; return; }
        Weights = Colors.Length switch
        {
            1 => new[] { 1f },
            2 => new[] { 0.6f, 0.4f },     // Primary 60%, Secondary 40%, as on the Mac
            _ => new[] { 0.4f, 0.3f, 0.3f },
        };
    }
}

/// One frame's values for the renderer.
public readonly record struct GlowFrame(float W, float H, float Corner, float Thickness, float Glow, Motion Mode,
    GlowAnimator Anim, float Presence);

/// Draws the light on the CPU, the same maths as the Mac's Metal shader.
/// Everything that only depends on the position along the rim (colors,
/// ripple, brightness patches) is worked out once per frame in a table.
public static class GlowRenderer
{
    const float TableStep = 2f;

    sealed class Tables { public float[] R = Array.Empty<float>(), G = Array.Empty<float>(), B = Array.Empty<float>(), Ripple = Array.Empty<float>(), Plasma = Array.Empty<float>(); public int Count; }

    static float Smooth(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }
    static float Fract(float x) => x - MathF.Floor(x);

    static float Segment(float f, float start, float len)
    {
        float dist = MathF.Abs(Fract(f - (start + len * 0.5f) + 0.5f) - 0.5f);
        return 1 - Smooth(len * 0.5f - 0.045f, len * 0.5f + 0.045f, dist);
    }

    static Tables Build(in GlowFrame f)
    {
        float per = 2 * (f.W + f.H);
        int count = (int)(per / TableStep) + 3;
        var t = new Tables { R = new float[count], G = new float[count], B = new float[count], Ripple = new float[count], Plasma = new float[count], Count = count };
        var a = f.Anim;
        int n = a.Colors.Length;
        float wp = a.WavePhase;
        for (int i = 0; i < count; i++)
        {
            float s = i * TableStep;
            Rgb c = a.Colors[0];
            if (n > 1)
            {
                float pos = Fract(s / per - a.ColorPhase), start = 0, sum = 0;
                Rgb acc = default;
                for (int j = 0; j < n; j++)
                {
                    float w = Segment(pos, start, a.Weights[j]);
                    acc += a.Colors[j] * w; sum += w; start += a.Weights[j];
                }
                c = acc / MathF.Max(sum, 1e-4f);
            }
            t.R[i] = c.R; t.G[i] = c.G; t.B[i] = c.B;
            t.Ripple[i] = 0.5f * MathF.Sin(s / 170f - wp * 1.9f) + 0.3f * MathF.Sin(s / 83f + wp * 1.3f) + 0.2f * MathF.Sin(s / 41f - wp * 2.7f);
            t.Plasma[i] = 0.62f * MathF.Sin(s / 300f - wp * 4f) + 0.38f * MathF.Sin(s / 175f + wp * 2.4f + 1.7f);
        }
        return t;
    }

    /// How far in from the edge the light can reach, in points.
    public static float Reach(LuneSettings s) =>
        s.ThicknessPoints * 1.15f * 1.55f * 3.2f + s.GlowPoints * 1.25f * 1.3f * 6f + 6f;

    /// Renders the region starting at (ox, oy) points into premultiplied
    /// BGRA pixels; each pixel covers `ptPerPx` points.
    public static void Render(int[] pixels, int pw, int ph, float ox, float oy, float ptPerPx, in GlowFrame frame)
    {
        if (frame.Presence <= 0.001f) { Array.Clear(pixels, 0, pw * ph); return; }
        var t = Build(frame);
        var f = frame;
        Parallel.For(0, ph, y => Row(pixels, pw, y, ox, oy, ptPerPx, f, t));
    }

    static void Row(int[] pixels, int pw, int y, float ox, float oy, float ptPerPx, GlowFrame f, Tables t)
    {
        float W = f.W, H = f.H, R = f.Corner;
        int mode = f.Mode switch { Motion.None => 0, Motion.Default => 1, Motion.Idle => 2, _ => 3 };
        float beat = f.Anim.Beat, energy = f.Anim.Energy;
        int n = f.Anim.Colors.Length;
        bool liquid = mode >= 1;   // the Mac's band: thin line + thick glowing body
        float amount = mode == 1 ? 0.55f : mode == 2 ? 0.22f : mode == 3 ? 0.3f + beat * 0.25f : 0;
        float T0 = f.Thickness * (mode == 3 ? 1 + energy * 0.15f + beat * 0.2f : 1);
        float G = f.Glow * (mode == 3 ? 1 + beat * 0.2f + energy * 0.1f : 1);
        float reach = liquid ? T0 * (1 + amount) * 3.2f + G * 6 + 2 : T0 * (1 + amount) + G * 4 + 2;
        float per = 2 * (W + H), phase = f.Anim.ColorPhase;
        float th0 = MathF.Atan2(-H * 0.5f, -W * 0.5f);
        float bump = mode == 3 ? 0.06f * beat : mode == 2 ? 0.2f : 0;
        var c1c = n > 1 ? f.Anim.Colors[1] : f.Anim.Colors[0];
        var c2c = n > 2 ? f.Anim.Colors[2] : c1c;
        float c0a = n == 2 ? 0.45f : -0.1f, c0b = n == 2 ? 0.75f : 0.8f;
        float k = Math.Clamp(MathF.Max(f.Glow, 4) * 0.35f, 3, 40);
        float kl = Math.Clamp(MathF.Max(f.Glow, 4) * 1.1f, 8, 140);
        float aa = 0.8f * ptPerPx;
        float level = mode == 2 ? 0.78f + 0.22f * MathF.Sin(f.Anim.Breath) : mode == 3 ? 0.95f + beat * 0.05f : 1;
        float presence = f.Presence * MathF.Min(1, level);
        var c0 = f.Anim.Colors[0];
        int last = t.Count - 1;
        float inv = 1 / TableStep;
        float py = oy + (y + 0.5f) * ptPerPx;
        int row = y * pw;
        Span<float> ed = stackalloc float[4];
        Span<float> es = stackalloc float[4];
        for (int x = 0; x < pw; x++)
        {
            float px = ox + (x + 0.5f) * ptPerPx;
            float cx = px - W * 0.5f, cy = py - H * 0.5f;
            float qx = MathF.Abs(cx) - W * 0.5f + R, qy = MathF.Abs(cy) - H * 0.5f + R;
            float outside = MathF.Sqrt(MathF.Max(qx, 0) * MathF.Max(qx, 0) + MathF.Max(qy, 0) * MathF.Max(qy, 0)) + MathF.Min(MathF.Max(qx, qy), 0) - R;
            float d = -outside;
            if (d < -1 || d > reach) { pixels[row + x] = 0; continue; }
            d = MathF.Max(d, 0);

            ed[0] = py; ed[1] = W - px; ed[2] = H - py; ed[3] = px;
            es[0] = px; es[1] = W + py; es[2] = W + H + (W - px); es[3] = 2 * W + H + (H - py);
            int e1 = 0;
            if (ed[1] < ed[e1]) e1 = 1;
            if (ed[2] < ed[e1]) e1 = 2;
            if (ed[3] < ed[e1]) e1 = 3;
            int ea = (e1 + 1) & 3, eb = (e1 + 3) & 3;
            int e2 = ed[ea] < ed[eb] ? ea : eb;
            float d1 = ed[e1], d2 = ed[e2];
            int i1 = Math.Clamp((int)(es[e1] * inv), 0, last), i2 = Math.Clamp((int)(es[e2] * inv), 0, last);
            float r = MathF.Exp(-(d2 - d1) / k);
            float w2 = r / (1 + r), w1 = 1 - w2;
            float dSoft = MathF.Max(0, d1 - k * MathF.Log(1 + r));

            float wave = w1 * t.Ripple[i1] + (w2 > 0.004f ? w2 * t.Ripple[i2] : w2 * t.Ripple[i1]);
            float cr = c0.R, cg = c0.G, cb = c0.B;
            float T, Gl, alphaLit, white;
            if (liquid)
            {
                // The band moves by angle around the screen centre, like a clock hand
                // (fast past the corners, slower mid-edge), as on the Mac.
                float sa = Fract((MathF.Atan2(cy, cx) - th0) / (2 * MathF.PI)) * per;
                float a0 = 2 * MathF.PI * (sa / per + phase);
                float g = (mode == 3 ? MathF.Sin(a0) + 0.12f : mode == 2 ? MathF.Sin(a0) + 0.35f : MathF.Sin(2 * a0) + 0.12f);
                if (n > 1)
                {
                    float ga = Smooth(c0a, c0b, g);
                    Rgb col = n > 2 ? c2c.Lerp(c1c, Smooth(0, 0.45f, ga)).Lerp(c0, Smooth(0.45f, 0.95f, ga)) : c1c.Lerp(c0, ga);
                    cr = col.R; cg = col.G; cb = col.B;
                }
                float gb = g + bump;
                float shapeLit = Smooth(0.15f, 0.85f, gb);
                float bandBright = Smooth(-0.24f, 0.04f, gb);
                float lobe = 0.5f + 0.5f * MathF.Sin(3 * 2 * MathF.PI * (sa / per + phase) + 1);
                shapeLit *= 0.82f + 0.18f * lobe;
                alphaLit = (0.4f + 0.6f * bandBright) * (0.45f + 0.55f * shapeLit);
                T = MathF.Max(0.75f, T0 * (0.2f + 0.6f * bandBright + 2.0f * shapeLit) * (1 + (0.1f + 0.45f * shapeLit) * wave));
                Gl = G * (0.25f + 1.4f * MathF.Pow(shapeLit, 1.2f));
                white = shapeLit;   // used below for the soft body
            }
            else
            {
                if (n > 1)
                {
                    if (w2 > 0.004f) { cr = t.R[i1] * w1 + t.R[i2] * w2; cg = t.G[i1] * w1 + t.G[i2] * w2; cb = t.B[i1] * w1 + t.B[i2] * w2; }
                    else { cr = t.R[i1]; cg = t.G[i1]; cb = t.B[i1]; }
                }
                alphaLit = 1;
                T = MathF.Max(0.75f, T0 * (1 + amount * 0.12f * wave) * 1.25f);
                Gl = G * 1.45f;
                white = 1;
            }
            float core = 1 - Smooth(T - aa, T + aa, d);
            float hot = MathF.Exp(-d / MathF.Max(0.6f, T * 0.45f)) * core;
            float xx = MathF.Max(0, MathF.Min(d, dSoft) - T);
            float halo = Gl > 0.3f ? (0.55f * MathF.Exp(-xx / (Gl * 0.45f)) + 0.07f * MathF.Exp(-xx / (Gl * 1.4f))) * (1 - Smooth(Gl * 2.2f, Gl * 4f, xx)) : 0;
            float alpha = Math.Clamp(MathF.Max(core, halo * (0.9f + 0.1f * core)), 0, 1) * alphaLit;
            float mixW;
            if (liquid) mixW = (0.05f + 0.18f * white) * core + hot * 0.05f;   // soft, lighter body; the thin line keeps the deep colour
            else { float centre = core * (1 - Smooth(0, MathF.Max(1, T * 0.85f), d)); mixW = MathF.Max(hot * 0.25f, centre * 0.55f); }
            float a = alpha * presence;
            if (a <= 0.002f) { pixels[row + x] = 0; continue; }
            float lr = MathF.Min(1, (cr + (1 - cr) * mixW) * level), lg = MathF.Min(1, (cg + (1 - cg) * mixW) * level), lb = MathF.Min(1, (cb + (1 - cb) * mixW) * level);
            int A = (int)(a * 255 + 0.5f), Rr = (int)(lr * a * 255 + 0.5f), Gg = (int)(lg * a * 255 + 0.5f), Bb = (int)(lb * a * 255 + 0.5f);
            pixels[row + x] = (A << 24) | (Rr << 16) | (Gg << 8) | Bb;
        }
    }
}

/// A click-through strip along one screen edge. The light is drawn at half
/// resolution and scaled up by the GPU; it's soft, so this looks the same.
public sealed class StripWindow : Window
{
    readonly Image image = new() { Stretch = Stretch.Fill };
    WriteableBitmap? bitmap;
    int[] pixels = Array.Empty<int>();
    public Rect Region { get; private set; }   // in points on its screen
    public const float PointsPerPixel = 2f;
    IntPtr hwnd;
    Native.Monitor? monitor;

    public StripWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        Title = "Lune light";
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
        Content = image;
        SourceInitialized += (_, _) => { hwnd = new WindowInteropHelper(this).Handle; Native.MakeClickThrough(hwnd); };
    }

    /// Moves the strip to `region` (points) on `monitor`.
    public void Place(Native.Monitor monitor, Rect region)
    {
        this.monitor = monitor;
        Region = region;
        int pw = Math.Max(1, (int)Math.Ceiling(region.Width / PointsPerPixel)), ph = Math.Max(1, (int)Math.Ceiling(region.Height / PointsPerPixel));
        if (bitmap is null || bitmap.PixelWidth != pw || bitmap.PixelHeight != ph)
        {
            bitmap = new WriteableBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32, null);
            pixels = new int[pw * ph];
            image.Source = bitmap;
        }
        if (hwnd == IntPtr.Zero) { var helper = new WindowInteropHelper(this); hwnd = helper.EnsureHandle(); }
        int x = monitor.Bounds.Left + (int)Math.Round(region.X * monitor.Scale), y = monitor.Bounds.Top + (int)Math.Round(region.Y * monitor.Scale);
        int w = (int)Math.Round(region.Width * monitor.Scale), h = (int)Math.Round(region.Height * monitor.Scale);
        // Twice: the first move can change the window's DPI, which resizes it.
        for (int i = 0; i < 2; i++) Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE);
    }

    public void Draw(in GlowFrame frame)
    {
        if (bitmap is null) return;
        GlowRenderer.Render(pixels, bitmap.PixelWidth, bitmap.PixelHeight, (float)Region.X, (float)Region.Y, PointsPerPixel, frame);
        bitmap.WritePixels(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight), pixels, bitmap.PixelWidth * 4, 0);
    }

    /// Shows the strip and puts it back in place (showing can move a WPF window).
    public void ShowStrip()
    {
        Show();
        if (monitor is not null) Place(monitor, Region);
    }

    public void KeepOnTop()
    {
        if (hwnd != IntPtr.Zero) Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}

/// Lights the edges of the chosen displays while music plays.
public sealed class EdgeLight
{
    sealed class Screen { public Native.Monitor Monitor = null!; public StripWindow[] Strips = Array.Empty<StripWindow>(); public float Reach; }
    readonly Model model;
    readonly List<Screen> screens = new();
    readonly GlowAnimator anim = new();
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    double lastFrame, lastTop;
    float presence;
    bool first = true, shown;
    string layoutKey = "";

    public EdgeLight(Model model)
    {
        this.model = model;
        model.Changed += Layout;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => { layoutKey = ""; Layout(); });
        CompositionTarget.Rendering += OnFrame;
        Layout();
    }

    List<Native.Monitor> Chosen()
    {
        var all = Native.Monitors();
        var d = model.Settings.Display;
        if (d == "all") return all;
        var match = all.Where(m => m.Device == d).ToList();
        return match.Count > 0 ? match : all.Take(1).ToList();
    }

    void Layout()
    {
        var monitors = Chosen();
        float reach = GlowRenderer.Reach(model.Settings);
        var key = string.Join(";", monitors.Select(m => $"{m.Device}:{m.Bounds.Left},{m.Bounds.Top},{m.Bounds.Width},{m.Bounds.Height}@{m.Scale}")) + $"|{reach:0}";
        if (key == layoutKey) return;
        layoutKey = key;
        foreach (var s in screens) foreach (var w in s.Strips) w.Close();
        screens.Clear();
        foreach (var m in monitors)
        {
            float W = (float)(m.Bounds.Width / m.Scale), H = (float)(m.Bounds.Height / m.Scale);
            float r = MathF.Min(reach, MathF.Min(W, H) / 2);
            var regions = new[]
            {
                new Rect(0, 0, W, r), new Rect(0, H - r, W, r),
                new Rect(0, r, r, Math.Max(1, H - 2 * r)), new Rect(W - r, r, r, Math.Max(1, H - 2 * r)),
            };
            var strips = regions.Select(region => { var w = new StripWindow(); w.Place(m, region); return w; }).ToArray();
            screens.Add(new Screen { Monitor = m, Strips = strips, Reach = r });
        }
        shown = false;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        double now = clock.Elapsed.TotalSeconds;
        var s = model.Settings;
        // 60 fps in every mode, as smooth as the Mac.
        double interval = 1 / 62.0;
        if (now - lastFrame < interval) return;
        float dt = (float)Math.Min(0.05, now - lastFrame);
        lastFrame = now;
        bool wantOn = s.Enabled && (!s.OnlyWhilePlaying || model.Media.Playing || model.Previewing);
        float target = wantOn ? 1 : 0;
        presence += (target - presence) * (first ? 1 : 1 - MathF.Exp(-dt / (target > presence ? 0.35f : 0.8f)));
        if (MathF.Abs(target - presence) < 0.002f) presence = target;
        if (presence <= 0 && !wantOn)
        {
            if (shown) { foreach (var sc in screens) foreach (var w in sc.Strips) w.Hide(); shown = false; }
            return;
        }
        if (!shown) { foreach (var sc in screens) foreach (var w in sc.Strips) w.ShowStrip(); shown = true; }
        var signal = model.Audio.Read();
        bool live = signal.Age < 0.5;
        var mode = s.Motion;
        var colors = model.Colors();
        if (s.FollowScreen && colors.Count == 4 && screens.Count > 0)
        {
            var b = screens[0].Monitor.Bounds; float per = 2f * (b.Width + b.Height);
            anim.EdgeWeights = new[] { b.Width / per, b.Height / per, b.Width / per, b.Height / per };
        }
        else anim.EdgeWeights = null;
        anim.Step(dt, mode, live ? signal.Energy : 0, live ? signal.Beat : 0, colors, first,
            live ? signal.Period : 0, live ? signal.Confidence : 0, signal.BeatTime, AudioLevel.Now, live ? signal.Section : 0);
        first = false;
        foreach (var sc in screens)
        {
            float W = (float)(sc.Monitor.Bounds.Width / sc.Monitor.Scale), H = (float)(sc.Monitor.Bounds.Height / sc.Monitor.Scale);
            // Louder parts of the song: a little thicker and glowier, as on the Mac.
            float dr = mode == Motion.MusicSync ? anim.Drive : 0;
            var frame = new GlowFrame(W, H, 10, s.ThicknessPoints * (1 + 0.15f * dr), s.GlowPoints * (1 + 0.25f * dr), mode, anim, presence);
            foreach (var w in sc.Strips) w.Draw(frame);
        }
        // Other always-on-top windows (the taskbar) can cover the light; take it back.
        if (now - lastTop > 0.5) { lastTop = now; foreach (var sc in screens) foreach (var w in sc.Strips) w.KeepOnTop(); }
    }

    /// A still frame of the whole screen, for previews and tests.
    public static WriteableBitmap Snapshot(LuneSettings s, IReadOnlyList<Rgb> colors, int width, int height, float seconds, Motion mode)
    {
        var anim = new GlowAnimator();
        anim.Step(0.016f, mode, 0.6f, 0.5f, colors, true);
        for (float t = 0; t < seconds; t += 0.05f) anim.Step(0.05f, mode, 0.6f, 0.5f, colors, false);
        var pixels = new int[width * height];
        GlowRenderer.Render(pixels, width, height, 0, 0, 1, new GlowFrame(width, height, 10, s.ThicknessPoints, s.GlowPoints, mode, anim, 1));
        var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return bmp;
    }
}
