using System.IO;
using System.Windows.Threading;
using LuneVers;

namespace Vers;

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
        if (Settings.Version < 2)
        {
            // 1.0 kept the lyrics behind windows with no black screen; match the Mac.
            Settings.BlackBackground = true; Settings.AboveWindows = true; Settings.Font = "Inter"; Settings.Version = 2;
            Store.Save("Vers", Settings);
        }
        Media = new MediaWatcher(ui);
        Media.TrackChanged += OnTrack;
        Media.StateChanged += () => Changed?.Invoke();
        saver = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => { saver!.Stop(); Store.Save("Vers", Settings); }, ui);
        saver.Stop();
        Watch(ui);
    }

    FileSystemWatcher? watcher;
    /// Lune's player can change these settings; pick up its changes.
    void Watch(Dispatcher ui)
    {
        try
        {
            Directory.CreateDirectory(VersLink.Folder);
            watcher = new FileSystemWatcher(VersLink.Folder, "settings.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
            var debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (t, _) => { ((DispatcherTimer)t!).Stop(); Reload(); }, ui);
            debounce.Stop();
            watcher.Changed += (_, _) => ui.BeginInvoke(() => { debounce.Stop(); debounce.Start(); });
            watcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    void Reload()
    {
        if (saver.IsEnabled) return;   // our own change is about to be saved
        var fresh = VersLink.Load();
        bool changed = false;
        foreach (var prop in typeof(VersSettings).GetProperties())
        {
            if (!prop.CanWrite) continue;
            var a = prop.GetValue(Settings); var b = prop.GetValue(fresh);
            if (!Equals(a, b)) { prop.SetValue(Settings, b); changed = true; }
        }
        if (changed) Changed?.Invoke();
    }

    public LyricStyle Style => Settings.AutoStyle ? AutoPick ?? Settings.Style : Settings.Style;

    /// The song position the lyrics follow, with the timing nudge.
    public double Time => preview is { } clock ? clock.Elapsed.TotalSeconds % 36 : Media.Position + (Media.Playing ? Settings.Offset : 0);
    /// Playing, or showing the built-in preview.
    public bool Playing => preview is not null || Media.Playing;
    public bool Previewing => preview is not null;
    System.Diagnostics.Stopwatch? preview;
    /// `Vers.exe --preview`: plays the demo lyrics with no music, for testing.
    public void StartPreview() { Import(Demo.Lyrics, "demo.lrc"); preview = System.Diagnostics.Stopwatch.StartNew(); Changed?.Invoke(); }

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
        VersLink.WriteAutoPick(AutoPick);
        Changed?.Invoke();
    }

    void OnTrack()
    {
        if (preview is not null) return;
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
