using System.Windows.Threading;
using LuneVers;

namespace Vers;

/// The four styles on Windows, picked from the Mac's nine. Timing is the same
/// in all of them; only the way the words arrive changes.
public enum LyricStyle { Ship, Fisheye, Codestyle, Constellation }

public sealed class VersSettings
{
    public bool AutoStyle { get; set; } = true;
    public LyricStyle Style { get; set; } = LyricStyle.Ship;
    public string Font { get; set; } = "Segoe UI Variable Display";
    public double Size { get; set; } = 1;
    public string Display { get; set; } = "main";
    public bool AutoColor { get; set; } = true;
    public string Lyric { get; set; } = "#F4F4F8";
    public string Highlight { get; set; } = "#FF4D8A";
    public string Secondary { get; set; } = "#9AA0B4";
    public bool OnlyWhilePlaying { get; set; } = true;
    public bool BlackBackground { get; set; }
    public bool AboveWindows { get; set; }
    public double Offset { get; set; }    // seconds; positive shows lyrics earlier

    public static readonly string[] Fonts = { "Segoe UI Variable Display", "Georgia", "Cascadia Code", "Bahnschrift" };
}

public static class StyleInfo
{
    public static string Name(LyricStyle s) => s.ToString();
    public static string Summary(LyricStyle s) => s switch
    {
        LyricStyle.Ship => "The last few words stack into a wall that drifts in 3D",
        LyricStyle.Fisheye => "Previous, current and next word under a glass lens",
        LyricStyle.Codestyle => "Each line typed out as code with syntax colors",
        _ => "Words become stars joined into a constellation",
    };

    /// Auto style: fast, dense lyrics (rap) get the punchy styles, slow songs
    /// the airy ones. The same song always gets the same style.
    public static LyricStyle? Suggest(IReadOnlyList<LyricWord> words, string title, string artist)
    {
        if (words.Count < 4) return null;
        var spans = new Dictionary<int, (double Start, double End)>();
        foreach (var w in words)
            spans[w.Line] = spans.TryGetValue(w.Line, out var s) ? (Math.Min(s.Start, w.Start), Math.Max(s.End, w.End)) : (w.Start, w.End);
        double singing = spans.Values.Sum(s => Math.Max(0.3, s.End - s.Start));
        double rate = words.Count / Math.Max(1, singing);
        LyricStyle[] group = rate > 3.0 ? new[] { LyricStyle.Ship, LyricStyle.Codestyle }
            : rate > 2.1 ? new[] { LyricStyle.Fisheye, LyricStyle.Ship, LyricStyle.Codestyle }
            : new[] { LyricStyle.Constellation, LyricStyle.Fisheye, LyricStyle.Constellation };
        ulong h = 1469598103934665603;
        foreach (var ch in (title + "|" + artist).ToLowerInvariant()) { h ^= ch; h *= 1099511628211; }
        return group[(int)(h % (ulong)group.Length)];
    }
}

/// Follows the song, finds its lyrics and colors.
public sealed class Engine
{
    public VersSettings Settings { get; }
    public MediaWatcher Media { get; }
    public LyricDocument? Lyrics { get; private set; }
    public string Status { get; private set; } = "Waiting for a song";
    public LyricStyle? AutoPick { get; private set; }
    public (Rgb Lyric, Rgb Highlight, Rgb Secondary)? AlbumColors { get; private set; }
    public event Action? Changed;
    readonly DispatcherTimer saver;
    CancellationTokenSource? cts;
    string key = "", artKey = "";
    bool imported;

    public Engine(Dispatcher ui)
    {
        Settings = Store.Load<VersSettings>("Vers");
        Media = new MediaWatcher(ui);
        Media.TrackChanged += OnTrack;
        Media.StateChanged += () => Changed?.Invoke();
        saver = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => { saver!.Stop(); Store.Save("Vers", Settings); }, ui);
        saver.Stop();
    }

    public LyricStyle Style => Settings.AutoStyle ? AutoPick ?? Settings.Style : Settings.Style;

    /// The song position the lyrics follow, with the timing nudge.
    public double Time => Media.Position + (Media.Playing ? Settings.Offset : 0);

    public (Rgb Lyric, Rgb Highlight, Rgb Secondary) Colors =>
        Settings.AutoColor && AlbumColors is { } a ? a : (Rgb.FromHex(Settings.Lyric), Rgb.FromHex(Settings.Highlight), Rgb.FromHex(Settings.Secondary));

    public void Update() { saver.Stop(); saver.Start(); Changed?.Invoke(); }

    public void Retry() { key = ""; imported = false; OnTrack(); }

    public void Import(string text, string name)
    {
        var doc = Lrc.Parse(text, Media.Duration);
        if (doc.Words.Count == 0) { Status = "No timestamps found. Use a standard or enhanced LRC file."; Changed?.Invoke(); return; }
        imported = true;
        SetLyrics(doc, doc.Exact ? $"Imported {name} · word timestamps" : $"Imported {name} · estimated word timing");
    }

    void SetLyrics(LyricDocument? doc, string status)
    {
        Lyrics = doc; Status = status;
        AutoPick = doc is null ? null : StyleInfo.Suggest(doc.Words, Media.Title, Media.Artist);
        Changed?.Invoke();
    }

    void OnTrack()
    {
        UpdateColors();
        if (!Media.HasTrack) { key = ""; SetLyrics(null, "Play a song in Spotify, Apple Music or a browser"); return; }
        var k = Media.TrackKey;
        if (k == key) return;
        key = k; imported = false;
        SetLyrics(null, "Looking for lyrics…");
        cts?.Cancel();
        var c = cts = new CancellationTokenSource();
        string title = Media.Title, artist = Media.Artist, album = Media.Album;
        double duration = Media.Duration;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await LyricsClient.FindAsync(title, artist, album, duration, c.Token);
                var doc = result.Synced is null ? null : Lrc.Parse(result.Synced, duration);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (c.IsCancellationRequested || key != k || imported) return;
                    if (doc is { Words.Count: > 0 }) SetLyrics(doc, doc.Exact ? "Synced · word timestamps" : "Synced · estimated word timing");
                    else SetLyrics(null, result.Instrumental ? "Instrumental" : "No synced lyrics for this song. Import an .lrc file from the menu.");
                });
            }
            catch (OperationCanceledException) { }
        });
    }

    void UpdateColors()
    {
        var art = Media.Art;
        var k = Media.TrackKey + "|" + (art?.Length ?? 0);
        if (k == artKey) return;
        artKey = k;
        if (art is null) return;
        Task.Run(() => Palette.Decode(art, 96) is { } image ? Palette.FromImage(image, 3) : null).ContinueWith(t =>
        {
            if (t.IsFaulted || t.Result is not { Count: > 0 } p || k != artKey) return;
            // Lyric text stays near white with a hint of the album; the
            // sung word takes the album's main color.
            var main = p[0];
            var second = p.Count > 1 ? p[1] : main;
            AlbumColors = (new Rgb(1, 1, 1).Lerp(main, 0.12f), main, new Rgb(0.62f, 0.64f, 0.72f).Lerp(second, 0.35f));
            Changed?.Invoke();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
