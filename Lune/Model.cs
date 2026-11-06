using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LuneVers;

namespace Lune;

/// The four animation modes, as on the Mac.
public enum Motion { Default, MusicSync, Idle, None }

public sealed class LuneSettings
{
    public bool Enabled { get; set; } = true;
    public Motion Motion { get; set; } = Motion.Default;
    public int ColorCount { get; set; } = 2;
    public double Thickness { get; set; } = 0.45;   // 0…1, hairline to broad band
    public double Glow { get; set; } = 0.7;         // 0…1, whisper to room wash
    public bool OverrideAlbum { get; set; }
    public bool FollowScreen { get; set; }
    public string Primary { get; set; } = "#FF1A4D";
    public string Secondary { get; set; } = "#1A6BFF";
    public string Tertiary { get; set; } = "#26D95C";
    public string Display { get; set; } = "main";   // "main", "all" or a display's device name
    public bool OnlyWhilePlaying { get; set; } = true;
    public bool KeepPlayerOnTop { get; set; }
    public double LyricsOffset { get; set; }
    public bool Welcomed { get; set; }
    public string LastVersion { get; set; } = "";

    public float ThicknessPoints => (float)(1.0 + Thickness * 17.0);
    public float GlowPoints => (float)(Glow * Glow * 110.0);
}

/// Shared state: settings, the song, its colors and the audio level.
public sealed class Model
{
    public LuneSettings Settings { get; }
    public MediaWatcher Media { get; }
    public AudioLevel Audio { get; } = new();
    public ScreenColors Screen { get; } = new();
    public List<Rgb>? AlbumPalette { get; private set; }
    public BitmapSource? Artwork { get; private set; }
    /// Shows the light while settings are being changed, even when paused.
    public bool Previewing { get; set; }
    public event Action? Changed;
    public event Action? ArtworkChanged;
    readonly DispatcherTimer saver;
    string artKey = "";

    public Model(Dispatcher ui)
    {
        Settings = Store.Load<LuneSettings>("Lune");
        Media = new MediaWatcher(ui);
        Media.TrackChanged += OnTrack;
        saver = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => { saver!.Stop(); Store.Save("Lune", Settings); }, ui);
        saver.Stop();
        SyncAudio();
    }

    /// Call after changing any setting.
    public void Update()
    {
        saver.Stop(); saver.Start();
        SyncAudio();
        Changed?.Invoke();
    }

    void SyncAudio()
    {
        // Music Sync and the player's pulse listen to what the PC is playing.
        Audio.Start();
        Screen.Run(Settings.FollowScreen, () => GlowRenderer.Reach(Settings));
    }

    void OnTrack()
    {
        var art = Media.Art;
        var key = Media.TrackKey + "|" + (art?.Length ?? 0);
        if (key == artKey) return;
        artKey = key;
        if (art is null) { Artwork = null; ArtworkChanged?.Invoke(); Changed?.Invoke(); return; }
        // Keep the previous palette until the new one is ready, so the light
        // cross-fades between albums instead of flashing to the manual colors.
        Task.Run(() =>
        {
            var image = Palette.Decode(art, 640);
            var small = Palette.Decode(art, 96);
            var palette = small is null ? null : Palette.FromImage(small, 3);
            return (image, palette);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted || key != artKey) return;
            Artwork = t.Result.image;
            if (t.Result.palette is { Count: > 0 } p) AlbumPalette = p;
            ArtworkChanged?.Invoke();
            Changed?.Invoke();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// The colors the light uses, in order.
    public List<Rgb> Colors()
    {
        var s = Settings;
        // Follow screen colors: one color per edge, taken from the screen.
        if (s.FollowScreen && Screen.Colors is { Count: 4 } edges) return edges;
        int n = Math.Clamp(s.ColorCount, 1, 3);
        if (!s.OverrideAlbum && AlbumPalette is { Count: > 0 } album) return album.Take(Math.Min(n, album.Count)).ToList();
        return new[] { Rgb.FromHex(s.Primary), Rgb.FromHex(s.Secondary), Rgb.FromHex(s.Tertiary) }.Take(n).ToList();
    }

    /// The main accent color for the player.
    public Rgb Accent => Colors().FirstOrDefault(new Rgb(1, 0.2f, 0.4f));
}
