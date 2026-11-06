using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LuneVers;

namespace Vers;

/// Draws the lyrics in the chosen style. Everything is worked out from the
/// song position each frame, so pausing, seeking and timing nudges just work.
public sealed class LyricsCanvas : FrameworkElement
{
    readonly Engine engine;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    double presence, lastFrame, dark;
    bool keptAwake;
    /// The window paints the black itself (the fast, opaque window).
    public bool WindowIsBlack { get; set; }
    /// 0…1: how far the black background has faded in.
    public double Dark => FixedPresence >= 0 ? (engine.Settings.BlackBackground ? 1 : 0) : dark;
    public event Action<bool>? DarkChanged;
    bool wasDark;
    double darkSince = -1;
    /// The song whose black screen was closed (Esc or a click); it stays off until the next song.
    object? dismissedFor;
    public void Dismiss() { dismissedFor = engine.Lyrics; }
    public double? FixedTime { get; set; }       // for snapshots
    public double FixedPresence { get; set; } = -1;

    public LyricsCanvas(Engine engine)
    {
        this.engine = engine;
        IsHitTestVisible = false;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Animated);
        CompositionTarget.Rendering += OnFrame;
    }

    void OnFrame(object? sender, EventArgs e) { if (IsVisible) Tick(); }

    /// Advances the fades. Also called by the black window while it's hidden.
    public void Tick()
    {
        double now = clock.Elapsed.TotalSeconds;
        // 60 fps whenever lyrics are on screen; a slow tick when nothing shows.
        bool busy = presence > 0.001 || dark > 0.001 || Busy();
        if (now - lastFrame < (busy ? 1 / 62.0 : 1 / 10.0)) return;
        double dt = Math.Min(0.1, now - lastFrame);
        lastFrame = now;
        var s = engine.Settings;
        double target = s.Visible && (!s.OnlyWhilePlaying || engine.Playing) ? 1 : 0;
        bool awake = s.KeepAwake && engine.Playing;
        if (awake != keptAwake) { keptAwake = awake; Native.KeepAwake(awake); }
        presence += (target - presence) * (1 - Math.Exp(-dt / (target > presence ? 0.25 : 0.6)));
        if (Math.Abs(target - presence) < 0.002) presence = target;
        // Black background fades in while a song with lyrics is playing, as on the Mac.
        // Never in the first 20 s after Vers starts (so it can't black out a PC as
        // Windows boots), and never again for a song whose black screen was closed.
        bool settled = now > 20 || engine.Previewing;
        double wantDark = settled && s.BlackBackground && s.Visible && engine.Playing && engine.Lyrics is { Words.Count: > 0 } && !ReferenceEquals(dismissedFor, engine.Lyrics) ? 1 : 0;
        dark += (wantDark - dark) * (1 - Math.Exp(-dt / 0.35));
        if (Math.Abs(wantDark - dark) < 0.002) dark = wantDark;
        bool isDark = dark >= 0.5;
        if (isDark != wasDark) { wasDark = isDark; darkSince = isDark ? now : -1; DarkChanged?.Invoke(isDark); }
        InvalidateVisual();
    }

    bool Busy()
    {
        var doc = engine.Lyrics;
        if (dark is > 0 and < 1) return true;
        if (doc is null || presence is > 0 and < 1) return presence is > 0 and < 1;
        double t = engine.Time;
        int i = Lrc.LastStarted(doc.Words, t);
        return engine.Style == LyricStyle.Ship || (i >= 0 && t - doc.Words[i].Start < 0.6) || (i + 1 < doc.Words.Count && doc.Words[i + 1].Start - t < 0.1);
    }

    double Base => 56 * engine.Settings.Size * Math.Min(1.4, Math.Max(0.7, ActualHeight / 700));
    Typeface Face(FontWeight weight) => new(LuneVers.Fonts.Family(engine.Settings.Font), FontStyles.Normal, weight, FontStretches.Normal);
    static readonly Typeface Mono = new(new FontFamily("Cascadia Code, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    FormattedText Text(string text, double size, Brush brush, Typeface face) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, Math.Max(1, size), brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    static SolidColorBrush Paint(Rgb c, double a) { var b = new SolidColorBrush(c.WithAlpha(Math.Clamp(a, 0, 1))); b.Freeze(); return b; }
    static double Clamp01(double x) => Math.Clamp(x, 0, 1);
    static double EaseOut(double x) { x = Clamp01(x); return 1 - Math.Pow(1 - x, 3); }
    static double Pop(double x) { x = Clamp01(x); const double c1 = 1.70158, c3 = c1 + 1; return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2); }
    static double Hash(int a, int b) { uint h = (uint)(a * 374761393 + b * 668265263); h = (h ^ (h >> 13)) * 1274126177; return (h ^ (h >> 16)) / (double)uint.MaxValue; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var s = engine.Settings;
        double fade = FixedPresence >= 0 ? FixedPresence : presence;
        if (Dark > 0.001 && !WindowIsBlack) dc.DrawRectangle(Paint(new Rgb(0, 0, 0), Dark), null, new Rect(0, 0, w, h));
        // How to leave the black screen, shown for a few seconds each time it appears.
        double hintAge = darkSince < 0 ? -1 : clock.Elapsed.TotalSeconds - darkSince;
        if (FixedPresence < 0 && hintAge is >= 0 and < 6)
        {
            double ha = Clamp01(Math.Min(hintAge / 0.4, (6 - hintAge) / 0.8)) * 0.75;
            var hint = Text("Space  pause     Esc or click  close the black screen", 15, Paint(new Rgb(1, 1, 1), ha), Face(FontWeights.Medium));
            dc.DrawText(hint, new Point((w - hint.Width) / 2, h - 60));
        }
        var doc = engine.Lyrics;
        if (doc is null || doc.Words.Count == 0 || fade <= 0.001) return;
        double t = FixedTime ?? engine.Time;
        var words = doc.Words;
        int i = Lrc.LastStarted(words, t);
        if (i < 0) return;
        // After a line, fade out during long instrumental gaps.
        double gapFade = 1;
        double sinceEnd = t - words[i].End;
        if (sinceEnd > 2.5 && (i + 1 >= words.Count || words[i + 1].Start - t > 0.6)) gapFade = Clamp01(1 - (sinceEnd - 2.5) / 0.8);
        if (i + 1 < words.Count && words[i + 1].Start - t < 0.5 && gapFade < 1) gapFade = Math.Max(gapFade, 1 - (words[i + 1].Start - t) / 0.5);
        double alpha = fade * gapFade;
        if (alpha <= 0.001) return;
        dc.PushOpacity(alpha);
        var colors = engine.Colors;
        switch (FixedStyle ?? engine.Style)
        {
            case LyricStyle.Ship: Ship(dc, w, h, words, i, t, colors, false); break;
            case LyricStyle.Visual: Ship(dc, w, h, words, i, t, colors, true); break;
            case LyricStyle.Fisheye: Fisheye(dc, w, h, words, i, t, colors, false); break;
            case LyricStyle.FisheyeVisual: Fisheye(dc, w, h, words, i, t, colors, true); break;
            case LyricStyle.Codestyle: Code(dc, w, h, doc, words, i, t, colors, false); break;
            case LyricStyle.CodestyleVisual: Code(dc, w, h, doc, words, i, t, colors, true); break;
            case LyricStyle.Branches: Branches(dc, w, h, doc, words, i, t, colors); break;
            case LyricStyle.Web: Web(dc, w, h, doc, words, i, t, colors); break;
            default: Constellation(dc, w, h, doc, words, i, t, colors); break;
        }
        dc.Pop();
    }

    public LyricStyle? FixedStyle { get; set; }

    /// The last few words stack into a wall that drifts in 3D: each new word
    /// pops in at the bottom and pushes the others up and away.
    void Ship(DrawingContext dc, double w, double h, List<LyricWord> words, int i, double t, (Rgb Lyric, Rgb Highlight, Rgb Secondary) c, bool icons)
    {
        double clockT = clock.Elapsed.TotalSeconds;
        double pop = Pop((t - words[i].Start) / 0.24);
        // The wall leans and sways slowly, as if seen in perspective.
        double sway = Math.Sin(clockT * 0.35) * 5, lean = Math.Sin(clockT * 0.21 + 1) * 4;
        var m = Matrix.Identity;
        m.Translate(-w / 2, -h * 0.6);
        m.Skew(lean, 0);
        m.Rotate(sway * 0.4);
        m.Translate(w / 2, h * 0.6);
        dc.PushTransform(new MatrixTransform(m));
        double y = h * 0.62;
        for (int age = 0; age < 7 && i - age >= 0; age++)
        {
            var word = words[i - age];
            double size = Base * 1.25 * Math.Pow(0.8, age) * (age == 0 ? 0.6 + 0.4 * pop : 1);
            double opacity = age == 0 ? EaseOut((t - word.Start) / 0.12) : 0.9 * Math.Pow(0.7, age);
            var col = age == 0 ? c.Highlight : c.Lyric.Lerp(c.Secondary, (float)Math.Min(1, age / 4.0));
            var ft = Text(word.Text, size, Paint(col, opacity), Face(age == 0 ? FontWeights.Bold : FontWeights.SemiBold));
            // Older words drift right and back, like a wall turning away.
            double x = w / 2 - ft.Width / 2 + age * Base * 0.18 * (1 - (age == 1 ? 1 - pop : 0));
            dc.DrawText(ft, new Point(x, y - ft.Height));
            // Visual: an icon pops in beside words that have one.
            if (icons && Symbols.For(word.Text) is { } icon)
            {
                double ip = Pop((t - word.Start) / 0.3);
                var emoji = Emoji(icon, size * 0.85 * ip, opacity, col);
                dc.DrawText(emoji, new Point(x + ft.Width + size * 0.2, y - ft.Height + (ft.Height - emoji.Height) / 2));
            }
            double step = ft.Height * 0.92;
            y -= age == 0 ? step * pop : step;
        }
        dc.Pop();
    }

    /// Previous, current and next word, the current one magnified under a lens.
    void Fisheye(DrawingContext dc, double w, double h, List<LyricWord> words, int i, double t, (Rgb Lyric, Rgb Highlight, Rgb Secondary) c, bool icons)
    {
        double p = EaseOut((t - words[i].Start) / 0.22);
        double cx = w / 2, cy = h / 2, r = Base * 1.55;
        // The lens: clear in the middle, bright at the rim, with a highlight.
        var lens = new RadialGradientBrush { GradientOrigin = new Point(0.4, 0.35) };
        lens.GradientStops.Add(new GradientStop(c.Highlight.WithAlpha(0.05), 0));
        lens.GradientStops.Add(new GradientStop(c.Highlight.WithAlpha(0.08), 0.75));
        lens.GradientStops.Add(new GradientStop(Ui.White(0.16), 1));
        lens.Freeze();
        dc.DrawEllipse(lens, new Pen(Paint(new Rgb(1, 1, 1), 0.35), 1.5), new Point(cx, cy), r, r);
        dc.DrawEllipse(Paint(new Rgb(1, 1, 1), 0.12), null, new Point(cx - r * 0.38, cy - r * 0.45), r * 0.28, r * 0.12);

        var cur = Text(words[i].Text, Base * 1.2 * (0.82 + 0.18 * Pop((t - words[i].Start) / 0.24)), Paint(c.Highlight, 1), Face(FontWeights.Bold));
        double fit = Math.Min(1, r * 1.8 / Math.Max(1, cur.Width));
        if (fit < 1) cur = Text(words[i].Text, Base * 1.2 * fit, Paint(c.Highlight, 1), Face(FontWeights.Bold));
        double slide = (1 - p) * Base * 1.4;
        dc.DrawText(cur, new Point(cx - cur.Width / 2 + slide, cy - cur.Height / 2));
        if (icons && Symbols.For(words[i].Text) is { } icon)
        {
            var emoji = Emoji(icon, Base * 0.7 * Pop((t - words[i].Start) / 0.3), 1, c.Highlight);
            dc.DrawText(emoji, new Point(cx - emoji.Width / 2 + slide, cy - r - emoji.Height * 0.55));
        }
        if (i > 0)
        {
            var prev = Text(words[i - 1].Text, Base * 0.48, Paint(c.Lyric, 0.6), Face(FontWeights.Medium));
            dc.DrawText(prev, new Point(cx - r - Base * 0.35 - prev.Width + slide, cy - prev.Height / 2));
        }
        if (i + 1 < words.Count)
        {
            var next = Text(words[i + 1].Text, Base * 0.48, Paint(c.Secondary, 0.5), Face(FontWeights.Medium));
            dc.DrawText(next, new Point(cx + r + Base * 0.35 + slide, cy - next.Height / 2));
        }
    }

    /// Each line typed out as a line of code, with the sung part appearing
    /// letter by letter and the earlier lines above it like an editor.
    void Code(DrawingContext dc, double w, double h, LyricDocument doc, List<LyricWord> words, int i, double t, (Rgb Lyric, Rgb Highlight, Rgb Secondary) c, bool icons)
    {
        var lines = doc.Lines();
        int li = lines.FindIndex(l => l[0].Line == words[i].Line);
        if (li < 0) return;
        double size = Base * 0.5, row = size * 1.6;
        string[] keywords = { "let", "const", "var", "val" };
        var keyBrush = Paint(c.Highlight, 1);
        var nameBrush = Paint(c.Lyric, 0.95);
        var punct = Paint(c.Secondary, 0.8);
        var str = Paint(c.Secondary.Lerp(c.Highlight, 0.35f), 1);
        var num = Paint(c.Secondary, 0.4);
        int firstLine = Math.Max(0, li - 4);
        double width = Base * 9.5;
        double x0 = (w - width) / 2, y0 = h / 2 - (li - firstLine) * row;
        for (int k = firstLine; k <= li; k++)
        {
            var line = lines[k];
            string text = string.Join(" ", line.Select(x => x.Text));
            int shown = text.Length;
            if (k == li)
            {
                shown = 0;
                foreach (var word in line)
                {
                    if (word.Start > t) break;
                    double f = Clamp01((t - word.Start) / Math.Max(0.05, word.End - word.Start));
                    int start = shown == 0 ? 0 : shown + 1;
                    shown = start + (int)Math.Round(word.Text.Length * (word.End <= t ? 1 : f));
                }
                shown = Math.Min(shown, text.Length);
            }
            double dim = k == li ? 1 : 0.35 + 0.15 * (k - firstLine) / Math.Max(1, li - firstLine);
            string kw = keywords[(int)(Hash(k, 7) * keywords.Length) % keywords.Length];
            var letters = new string(line[0].Text.Where(char.IsLetter).ToArray()).ToLowerInvariant();
            string name = (letters.Length > 0 ? letters : "line") + "_" + (k + 1);
            var parts = new (string Text, Brush Brush)[]
            {
                ($"{k + 1,3}  ", num), (kw + " ", keyBrush), (name, nameBrush), (" = ", punct), ("\"", str), (text[..shown], str),
                (k == li && shown < text.Length ? "" : "\"", str), (k == li && shown < text.Length ? "" : ";", punct),
            };
            double x = x0, y = y0 + (k - firstLine) * row;
            dc.PushOpacity(dim);
            // Codestyle Visual: an icon before rows that have one.
            if (icons && line.Select(word => Symbols.For(word.Text)).FirstOrDefault(e => e is not null) is { } rowIcon)
            {
                var emoji = Emoji(rowIcon, size, 1, c.Highlight);
                dc.DrawText(emoji, new Point(x - emoji.Width - size * 0.5, y));
            }
            foreach (var (s, b) in parts)
            {
                if (s.Length == 0) continue;
                var ft = Text(s, size, b, Mono);
                dc.DrawText(ft, new Point(x, y));
                x += ft.WidthIncludingTrailingWhitespace;
            }
            if (k == li && (clock.Elapsed.TotalSeconds % 1.0) < 0.6) dc.DrawRectangle(keyBrush, null, new Rect(x + 2, y + size * 0.15, size * 0.5, size * 1.15));
            dc.Pop();
        }
    }

    FormattedText Emoji(string icon, double size, double opacity, Rgb tint)
    {
        var ft = new FormattedText(icon, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, EmojiFace, Math.Max(1, size), Paint(tint, opacity), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return ft;
    }
    static readonly Typeface EmojiFace = new(new FontFamily("Segoe UI Emoji"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    static List<LyricWord>? CurrentLine(LyricDocument doc, List<LyricWord> words, int i, out int li)
    {
        var lines = doc.Lines();
        li = lines.FindIndex(l => l[0].Line == words[i].Line);
        return li < 0 ? null : lines[li];
    }

    void Label(DrawingContext dc, LyricWord word, Point at, bool active, double t, (Rgb Lyric, Rgb Highlight, Rgb Secondary) c, double size)
    {
        double appear = EaseOut((t - word.Start) / 0.25);
        double scale = active ? 1 : 0.62;
        var ft = Text(word.Text, size * scale * (0.7 + 0.3 * Pop((t - word.Start) / 0.3)), Paint(active ? c.Highlight : c.Lyric, appear * (active ? 1 : 0.8)), Face(active ? FontWeights.SemiBold : FontWeights.Medium));
        dc.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
    }

    /// A wavy stem grows through the line and every word sprouts a branch.
    void Branches(DrawingContext dc, double w, double h, LyricDocument doc, List<LyricWord> words, int i, double t, (Rgb Lyric, Rgb Highlight, Rgb Secondary) c)
    {
        var line = CurrentLine(doc, words, i, out int li);
        if (line is null) return;
        double b = Base, n = Math.Max(1, line.Count);
        double x0 = w * 0.18, spacing = Math.Min(b * 2.1, w * 0.64 / n);
        double cy = h * 0.56, amp = b * 0.3, wave = spacing * 1.15, phase = Hash(li, 50) * Math.PI * 2;
        double StemY(double x) => cy + Math.Sin((x - x0) / wave * Math.PI + phase) * amp;
        double F = b * 0.62, headX = x0;
        var stemPen = new Pen(Paint(c.Highlight, 0.45), Math.Max(1.5, b * 0.05)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var labels = new List<(LyricWord Word, Point At, bool Active)>();
        for (int k = 0; k < line.Count; k++)
        {
            var word = line[k];
            if (word.Start > t) break;
            double bx = x0 + (k + 0.5) * spacing;
            var root = new Point(bx, StemY(bx));
            bool up = (k % 2 == 0) != (Hash(li * 13 + k, 51) > 0.75);
            double len = b * (1.2 + Hash(li * 13 + k, 52) * 1.5);
            var tip = new Point(bx + (Hash(li * 13 + k, 53) - 0.5) * spacing * 0.7, root.Y + (up ? -len : len));
            double grow = EaseOut((t - word.Start) / 0.35);
            bool active = t < word.End || k == line.Count - 1 && i == words.IndexOf(word);
            var end = new Point(root.X + (tip.X - root.X) * grow, root.Y + (tip.Y - root.Y) * grow);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(root, false, false);
                ctx.QuadraticBezierTo(new Point(root.X, (root.Y + end.Y) / 2), end, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(Paint(c.Secondary, 0.6), Math.Max(1, b * 0.03)), g);
            dc.DrawEllipse(Paint(c.Highlight, grow), null, end, b * 0.07, b * 0.07);
            labels.Add((word, new Point(end.X, end.Y + (up ? -1 : 1) * (F * 0.5 + b * 0.25)), word == words[i]));
            headX = word == words[i] ? bx + Clamp01((t - word.Start) / Math.Max(0.1, word.End - word.Start)) * spacing * 0.5 : Math.Max(headX, bx);
        }
        // The stem, drawn up to where the singing has reached.
        var stem = new StreamGeometry();
        using (var ctx = stem.Open())
        {
            ctx.BeginFigure(new Point(x0, StemY(x0)), false, false);
            for (double x = x0 + 4; x <= headX; x += 4) ctx.LineTo(new Point(x, StemY(x)), true, true);
        }
        stem.Freeze();
        dc.DrawGeometry(null, stemPen, stem);
        foreach (var (word, at, active) in labels) Label(dc, word, at, active, t, c, F);
    }

    /// A twelve-spoke spider web; each word spins a strand out from the hub.
    void Web(DrawingContext dc, double w, double h, LyricDocument doc, List<LyricWord> words, int i, double t, (Rgb Lyric, Rgb Highlight, Rgb Secondary) c)
    {
        var line = CurrentLine(doc, words, i, out int li);
        if (line is null) return;
        var hub = new Point(w / 2, h * 0.52);
        double R = Math.Min(h * 0.36, w * 0.28), F = Base * 0.55;
        // The faint web: spokes and rings.
        var faint = new Pen(Paint(c.Secondary, 0.16), 1);
        for (int k = 0; k < 12; k++)
        {
            double a = k / 12.0 * 2 * Math.PI - Math.PI / 2;
            dc.DrawLine(faint, hub, new Point(hub.X + Math.Cos(a) * R, hub.Y + Math.Sin(a) * R));
        }
        foreach (var ring in new[] { 0.35, 0.6, 0.85 })
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int k = 0; k <= 12; k++)
                {
                    double a = k / 12.0 * 2 * Math.PI - Math.PI / 2;
                    var p = new Point(hub.X + Math.Cos(a) * R * ring, hub.Y + Math.Sin(a) * R * ring);
                    if (k == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, faint, g);
        }
        int start = (int)(Hash(li, 41) * 12);
        var strand = new Pen(Paint(c.Secondary, 0.65), Math.Max(1, Base * 0.03));
        for (int k = 0; k < line.Count; k++)
        {
            var word = line[k];
            if (word.Start > t) break;
            int spoke = (start + k * 5) % 12;
            double ring = 0.45 + 0.5 * Hash(li * 19 + k, 42);
            double a = spoke / 12.0 * 2 * Math.PI - Math.PI / 2;
            var p = new Point(hub.X + Math.Cos(a) * R * ring, hub.Y + Math.Sin(a) * R * ring);
            double grow = EaseOut((t - word.Start) / 0.4);
            var end = new Point(hub.X + (p.X - hub.X) * grow, hub.Y + (p.Y - hub.Y) * grow);
            bool active = word == words[i] && t < word.End + 0.4;
            dc.DrawLine(active ? new Pen(Paint(c.Highlight, 0.8), Math.Max(1.5, Base * 0.04)) : strand, hub, end);
            dc.DrawEllipse(Paint(c.Highlight, grow), null, end, Base * 0.07, Base * 0.07);
            var off = Math.Abs(Math.Cos(a)) > 0.8 ? new Vector(Math.Sign(Math.Cos(a)) * (F * word.Text.Length * 0.32 + F * 0.5), 0) : new Vector(0, Math.Sign(Math.Sin(a)) * (F * 0.9));
            Label(dc, word, end + off * (active ? 1 : 0.7), active, t, c, F);
        }
        dc.DrawEllipse(Paint(c.Highlight, 0.9), null, hub, Base * 0.09, Base * 0.09);
    }

    /// The line's words become stars joined into a constellation as they're sung.
    void Constellation(DrawingContext dc, double w, double h, LyricDocument doc, List<LyricWord> words, int i, double t, (Rgb Lyric, Rgb Highlight, Rgb Secondary) c)
    {
        var lines = doc.Lines();
        int li = lines.FindIndex(l => l[0].Line == words[i].Line);
        if (li < 0) return;
        double since = t - lines[li][0].Start;
        if (li > 0 && since < 0.6) DrawStars(dc, w, h, lines[li - 1], li - 1, t, c, 1 - since / 0.6);
        DrawStars(dc, w, h, lines[li], li, t, c, EaseOut(since / 0.3));
    }

    void DrawStars(DrawingContext dc, double w, double h, List<LyricWord> line, int li, double t, (Rgb Lyric, Rgb Highlight, Rgb Secondary) c, double alpha)
    {
        int n = line.Count;
        double b = Base;
        double span = Math.Min(w * 0.7, Math.Max(1, n - 1) * b * 1.6);
        double cx = w / 2, cy = h * (0.5 + (Hash(li, 2) - 0.5) * 0.14), A = h * 0.13;
        int shape = (int)(Hash(li, 3) * 3);
        var points = new Point[n];
        for (int j = 0; j < n; j++)
        {
            double u = n == 1 ? 0.5 : j / (double)(n - 1);
            double x = cx - span / 2 + span * u;
            double y = shape switch
            {
                0 => cy + A * 0.5 - Math.Sin(Math.PI * u) * A,
                1 => cy + Math.Sin(u * Math.PI * 2.3) * A * 0.55,
                _ => cy + (u - 0.5) * A * 0.9 + Math.Sin(u * 9) * A * 0.15,
            };
            points[j] = new Point(x + (Hash(li * 31 + j, 5) - 0.5) * b * 0.4, y + (Hash(li * 17 + j, 9) - 0.5) * b * 0.5);
        }
        dc.PushOpacity(Clamp01(alpha));
        var linePen = new Pen(Paint(c.Secondary, 0.55), Math.Max(1, b * 0.03));
        for (int j = 1; j < n; j++)
        {
            double k = Clamp01((t - line[j].Start) / 0.3);
            if (k <= 0) break;
            var from = points[j - 1];
            var to = new Point(from.X + (points[j].X - from.X) * k, from.Y + (points[j].Y - from.Y) * k);
            dc.DrawLine(linePen, from, to);
        }
        for (int j = 0; j < n; j++)
        {
            var word = line[j];
            if (word.Start > t) break;
            double appear = EaseOut((t - word.Start) / 0.3);
            bool active = t < word.End;
            double radius = b * (active ? 0.42 : 0.28) * (0.6 + 0.4 * Pop((t - word.Start) / 0.35));
            var halo = new RadialGradientBrush(c.Highlight.WithAlpha((active ? 0.55 : 0.3) * appear), Colors.Transparent);
            halo.Freeze();
            dc.DrawEllipse(halo, null, points[j], radius * 2.4, radius * 2.4);
            dc.DrawEllipse(Paint(new Rgb(1, 1, 1).Lerp(c.Highlight, 0.25f), appear), null, points[j], b * 0.07, b * 0.07);
            var label = Text(word.Text, b * (active ? 0.5 : 0.42), Paint(active ? c.Highlight : c.Lyric, appear * (active ? 1 : 0.85)), Face(active ? FontWeights.SemiBold : FontWeights.Medium));
            bool below = Hash(li * 7 + j, 11) > 0.5;
            dc.DrawText(label, new Point(points[j].X - label.Width / 2, below ? points[j].Y + b * 0.25 : points[j].Y - b * 0.25 - label.Height));
        }
        dc.Pop();
    }
}

/// A frosted-free, click-through window that holds the lyrics.
public sealed class OverlayWindow : Window
{
    readonly Engine engine;
    public LyricsCanvas Canvas { get; }
    IntPtr hwnd;
    Native.Monitor? monitor;
    readonly System.Windows.Threading.DispatcherTimer layer;

    readonly bool opaque;
    byte alpha = 255;

    /// `opaque`: with the black background on, the window is a plain black
    /// window drawn by the graphics card and faded by the compositor. That's
    /// far smoother than a see-through window, which Windows copies back from
    /// the graphics card every frame.
    public OverlayWindow(Engine engine, bool opaque)
    {
        this.engine = engine;
        this.opaque = opaque;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = !opaque;
        Background = opaque ? Brushes.Black : Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Title = "Vers lyrics";
        Topmost = engine.Settings.AboveWindows;
        Canvas = new LyricsCanvas(engine);
        Content = Canvas;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeClickThrough(hwnd);
            if (opaque) { alpha = 0; Native.SetAlpha(hwnd, 0); }
        };
        Canvas.WindowIsBlack = opaque;
        // The black window only exists on screen while the black is meant to
        // show. If fading it ever failed on some PC, it still can't cover the
        // screen while nothing is playing.
        if (opaque)
        {
            void Sync()
            {
                if (hwnd == IntPtr.Zero) return;
                if (!IsVisible) Canvas.Tick();
                var a = (byte)Math.Round(Math.Clamp(Canvas.Dark, 0, 1) * 255);
                if (a != alpha) { alpha = a; Native.SetAlpha(hwnd, a); }
                if (a == 0 && IsVisible) Hide();
                else if (a > 0 && !IsVisible) { Show(); Reposition(); }
            }
            CompositionTarget.Rendering += (_, _) => Sync();
            new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(40), System.Windows.Threading.DispatcherPriority.Render, (_, _) => Sync(), Dispatcher).Start();
        }
        // While the screen is black it takes clicks and keys, so nothing hidden
        // gets clicked and Space pauses the song (handy with one monitor).
        Canvas.DarkChanged += dark =>
        {
            if (hwnd == IntPtr.Zero) return;
            Native.SetClickThrough(hwnd, !dark);
            Canvas.IsHitTestVisible = dark;
            if (dark) { Activate(); Focus(); }
        };
        if (!opaque) Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        // A click or Esc closes the black screen for this song, so nobody gets stuck.
        MouseDown += (_, _) => { if (Canvas.Dark >= 0.3) Canvas.Dismiss(); };
        KeyDown += (_, e) =>
        {
            if (Canvas.Dark < 0.3) return;
            if (e.Key == System.Windows.Input.Key.Escape) { Canvas.Dismiss(); e.Handled = true; return; }
            if (e.Key == System.Windows.Input.Key.Space || e.Key == System.Windows.Input.Key.MediaPlayPause) { engine.Media.PlayPause(); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Right) engine.Media.Next();
            else if (e.Key == System.Windows.Input.Key.Left) engine.Media.Previous();
        };
        layer = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(1), System.Windows.Threading.DispatcherPriority.Background, (_, _) => KeepLayer(), Dispatcher);
        layer.Start();
    }

    /// Covers the whole screen, like the Mac.
    public void Place(Native.Monitor m)
    {
        monitor = m;
        if (hwnd == IntPtr.Zero) hwnd = new WindowInteropHelper(this).EnsureHandle();
        var area = m.Bounds;
        for (int i = 0; i < 2; i++) Native.SetWindowPos(hwnd, IntPtr.Zero, area.Left, area.Top, area.Width, area.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        KeepLayer();
    }

    public void Reposition() { if (monitor is not null) Place(monitor); }

    void KeepLayer()
    {
        if (hwnd == IntPtr.Zero || !IsVisible) return;
        if (engine.Settings.AboveWindows) Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        else { Native.SetWindowPos(hwnd, new IntPtr(-2), 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); DesktopLayer.Place(hwnd); }
    }
}

static class Ui
{
    public static Color White(double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), 255, 255, 255);
}
