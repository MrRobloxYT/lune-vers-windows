using System.Diagnostics;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace LuneVers;

/// The song Windows says is playing, from any app that shows in the volume
/// flyout: Spotify, Apple Music, browsers and more. Position is smoothed
/// between Windows' updates so lyrics can follow it to the frame.
public sealed class MediaWatcher
{
    GlobalSystemMediaTransportControlsSessionManager? manager;
    GlobalSystemMediaTransportControlsSession? session;
    readonly Dispatcher ui;
    DispatcherTimer? timer;
    readonly Stopwatch clock = Stopwatch.StartNew();
    double anchorPosition, anchorTime;
    int propsVersion;

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Album { get; private set; } = "";
    public string Source { get; private set; } = "";
    public bool Playing { get; private set; }
    public double Duration { get; private set; }
    public byte[]? Art { get; private set; }
    public bool HasTrack => Title.Length > 0;
    public bool Available => manager is not null;
    public string TrackKey => $"{Source}|{Title}|{Artist}";

    /// Song, artist or cover changed.
    public event Action? TrackChanged;
    /// Play, pause or seek.
    public event Action? StateChanged;

    public MediaWatcher(Dispatcher ui) { this.ui = ui; }

    /// Song position in seconds, moving smoothly while playing.
    public double Position => Playing ? anchorPosition + (clock.Elapsed.TotalSeconds - anchorTime) : anchorPosition;

    public async Task StartAsync()
    {
        try { manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
        catch { manager = null; return; }
        manager.SessionsChanged += (_, _) => ui.BeginInvoke(Pick);
        manager.CurrentSessionChanged += (_, _) => ui.BeginInvoke(Pick);
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Pick(), ui);
        timer.Start();
        Pick();
    }

    /// Follows the session that is playing; otherwise the one Windows calls current.
    void Pick()
    {
        if (manager is null) return;
        GlobalSystemMediaTransportControlsSession? best = null;
        try
        {
            var sessions = manager.GetSessions();
            best = sessions.FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                   ?? manager.GetCurrentSession() ?? sessions.FirstOrDefault();
        }
        catch { }
        // Windows may hand back a new wrapper for the same app, so compare by app.
        if (best?.SourceAppUserModelId != session?.SourceAppUserModelId || (best is null) != (session is null)) Attach(best);
        ReadState();
    }

    void Attach(GlobalSystemMediaTransportControlsSession? next)
    {
        if (session is not null)
        {
            session.MediaPropertiesChanged -= OnProps;
            session.PlaybackInfoChanged -= OnPlayback;
            session.TimelinePropertiesChanged -= OnTimeline;
        }
        session = next;
        if (session is null)
        {
            Title = Artist = Album = Source = ""; Art = null; Playing = false; Duration = 0; anchorPosition = 0;
            TrackChanged?.Invoke(); StateChanged?.Invoke();
            return;
        }
        Source = FriendlyName(session.SourceAppUserModelId);
        session.MediaPropertiesChanged += OnProps;
        session.PlaybackInfoChanged += OnPlayback;
        session.TimelinePropertiesChanged += OnTimeline;
        _ = LoadProps();
    }

    void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => ui.BeginInvoke(() => _ = LoadProps());
    void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => ui.BeginInvoke(ReadState);
    void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => ui.BeginInvoke(ReadState);

    async Task LoadProps()
    {
        var s = session;
        if (s is null) return;
        int version = ++propsVersion;
        GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
        try { props = await s.TryGetMediaPropertiesAsync(); } catch { }
        if (props is null || version != propsVersion || !ReferenceEquals(s, session)) return;
        string title = props.Title ?? "", artist = props.Artist ?? "", album = props.AlbumTitle ?? "";
        if (artist.Length == 0) artist = props.AlbumArtist ?? "";
        bool changed = title != Title || artist != Artist || album != Album;
        Title = title; Artist = artist; Album = album;
        if (changed) { Art = null; TrackChanged?.Invoke(); }
        byte[]? art = null;
        try
        {
            if (props.Thumbnail is not null)
            {
                using var stream = await props.Thumbnail.OpenReadAsync();
                if (stream.Size > 64 && stream.Size < 20_000_000)
                {
                    using var reader = new DataReader(stream);
                    await reader.LoadAsync((uint)stream.Size);
                    art = new byte[stream.Size];
                    reader.ReadBytes(art);
                }
            }
        }
        catch { }
        if (version != propsVersion) return;
        if (art is not null && (Art is null || !art.AsSpan().SequenceEqual(Art))) { Art = art; TrackChanged?.Invoke(); }
        ReadState();
    }

    void ReadState()
    {
        var s = session;
        if (s is null) return;
        try
        {
            var info = s.GetPlaybackInfo();
            bool playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var tl = s.GetTimelineProperties();
            double position = tl.Position.TotalSeconds;
            Duration = Math.Max(0, (tl.EndTime - tl.StartTime).TotalSeconds);
            // Windows stamps each timeline update; count the time since then.
            var since = (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds;
            if (playing && since > 0 && since < 3600 && tl.LastUpdatedTime.Year > 2000) position += since;
            if (Duration > 0) position = Math.Min(position, Duration);
            double predicted = Position;
            bool stateChanged = playing != Playing;
            Playing = playing;
            double now = clock.Elapsed.TotalSeconds;
            // Jump on seeks; ease small drift in so lyrics never twitch.
            if (stateChanged || !playing || Math.Abs(predicted - position) > 0.25) anchorPosition = position;
            else anchorPosition = predicted + (position - predicted) * 0.5;
            anchorTime = now;
            if (stateChanged) StateChanged?.Invoke();
        }
        catch { }
    }

    public static string FriendlyName(string id)
    {
        if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (id.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) || id.Contains("iTunes", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
        if (id.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (id.Contains("msedge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (id.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (id.Contains("ZuneMusic", StringComparison.OrdinalIgnoreCase)) return "Media Player";
        var name = id.Split('!')[0];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1 && name.Contains('_')) name = name[(dot + 1)..].Split('_')[0];
        return name;
    }

    public async void PlayPause() { try { if (session is not null) await session.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (session is not null) await session.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (session is not null) await session.TrySkipPreviousAsync(); } catch { } }
    public async void Seek(double seconds)
    {
        try
        {
            if (session is null) return;
            var target = Math.Max(0, Duration > 0 ? Math.Min(Duration - 0.5, seconds) : seconds);
            if (await session.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(target).Ticks)) { anchorPosition = target; anchorTime = clock.Elapsed.TotalSeconds; }
        }
        catch { }
    }
}
