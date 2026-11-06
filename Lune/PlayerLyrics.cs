using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LuneVers;

namespace Lune;

/// Synced lyrics for the player, from LRCLIB.
public sealed class PlayerLyrics
{
    readonly Model model;
    CancellationTokenSource? cts;
    string key = "";
    double? demoTime;
    public List<List<LyricWord>> Lines { get; private set; } = new();
    public string Status { get; private set; } = "Looking for lyrics…";
    public event Action? Changed;

    public PlayerLyrics(Model model)
    {
        this.model = model;
        model.Media.TrackChanged += () => Refresh();
    }

    /// The song position the lyrics follow, with the user's timing nudge.
    public double Now => demoTime ?? model.Media.Position + model.Settings.LyricsOffset;

    public void Retry() => Refresh(true);

    public void Refresh(bool force = false)
    {
        if (demoTime is not null) return;
        var media = model.Media;
        if (!media.HasTrack)
        {
            cts?.Cancel(); key = ""; Lines = new(); Status = "Play a song in Spotify, Apple Music or a browser";
            Changed?.Invoke(); return;
        }
        var k = media.TrackKey;
        if (k == key && !force) return;
        key = k; Lines = new(); Status = "Looking for lyrics…"; Changed?.Invoke();
        cts?.Cancel();
        var c = cts = new CancellationTokenSource();
        string title = media.Title, artist = media.Artist, album = media.Album;
        double duration = media.Duration;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await LyricsClient.FindAsync(title, artist, album, duration, c.Token);
                var doc = result.Synced is null ? null : Lrc.Parse(result.Synced, duration);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (c.IsCancellationRequested || key != k) return;
                    if (doc is { Words.Count: > 0 }) { Lines = doc.Lines(); Status = ""; }
                    else Status = result.Instrumental ? "Instrumental" : "No synced lyrics for this song";
                    Changed?.Invoke();
                });
            }
            catch (OperationCanceledException) { }
        });
    }

    public void SetDemo(LyricDocument doc, double time) { demoTime = time; Lines = doc.Lines(); Status = ""; Changed?.Invoke(); }
}

/// The scrolling lyrics list: the current line is larger and fills in word
/// by word; the list glides to keep it centered and fades at the edges.
public sealed class LyricLinesView : FrameworkElement
{
    readonly PlayerLyrics lyrics;
    double scroll = -1, lastTick;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    static readonly Typeface Regular = new(Ui.Text, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    static readonly Typeface Bold = new(Ui.Text, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    const double RowHeight = 30;

    public LyricLinesView(PlayerLyrics lyrics)
    {
        this.lyrics = lyrics;
        ClipToBounds = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Animated);
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.22));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.78));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        OpacityMask = mask;
        lyrics.Changed += () => { scroll = -1; InvalidateVisual(); };
        CompositionTarget.Rendering += (_, _) => { if (IsVisible) InvalidateVisual(); };
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var lines = lyrics.Lines;
        if (lines.Count == 0)
        {
            var status = new FormattedText(lyrics.Status, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Regular, 15, Ui.Fill(0.55), dip) { MaxTextWidth = Math.Max(10, w), TextAlignment = TextAlignment.Center };
            dc.DrawText(status, new Point(0, (h - status.Height) / 2));
            return;
        }
        double t = lyrics.Now;
        int current = 0;
        for (int i = 0; i < lines.Count; i++) if (lines[i][0].Start <= t) current = i;
        double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.1, now - lastTick);
        lastTick = now;
        // A quick glide that settles without bouncing.
        scroll = scroll < 0 ? current : scroll + (current - scroll) * (1 - Math.Exp(-dt / 0.075));
        if (Math.Abs(scroll - current) < 0.001) scroll = current;
        int first = Math.Max(0, (int)(scroll - h / RowHeight / 2) - 1), last = Math.Min(lines.Count - 1, (int)(scroll + h / RowHeight / 2) + 1);
        for (int i = first; i <= last; i++)
        {
            double y = h / 2 + (i - scroll) * RowHeight;
            int distance = Math.Abs(i - current);
            bool isCurrent = distance == 0;
            var line = lines[i];
            string text = string.Join(" ", line.Select(x => x.Text));
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, isCurrent ? Bold : Regular, isCurrent ? 17 : 14,
                Ui.Fill(isCurrent ? 0.42 : Math.Max(0.12, 0.5 - distance * 0.1)), dip)
            { MaxTextWidth = Math.Max(10, w), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            if (isCurrent)
            {
                // Words light up the moment they're sung.
                int pos = 0;
                foreach (var word in line)
                {
                    if (word.Start <= t) ft.SetForegroundBrush(Brushes.White, pos, Math.Min(word.Text.Length + 1, text.Length - pos));
                    pos += word.Text.Length + 1;
                    if (pos >= text.Length) break;
                }
            }
            dc.DrawText(ft, new Point(0, y - ft.Height / 2));
        }
    }
}
