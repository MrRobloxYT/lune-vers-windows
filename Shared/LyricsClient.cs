using System.Net.Http;
using System.Text.Json;

namespace LuneVers;

/// Finds synced lyrics on LRCLIB (free, no account): an exact match first,
/// then searches with the full and the plain title.
public static class LyricsClient
{
    static readonly HttpClient Http = MakeClient();

    static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("LuneVers/1.0 (Windows)");
        return c;
    }

    public sealed record Result(string? Synced, bool Instrumental);

    static async Task<JsonDocument?> Get(string path, CancellationToken ct, params (string Key, string Value)[] query)
    {
        var url = "https://lrclib.net/api/" + path + "?" + string.Join("&", query.Select(q => q.Key + "=" + Uri.EscapeDataString(q.Value)));
        try
        {
            using var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    static string? Synced(JsonElement e) =>
        e.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } text ? text : null;

    static string? Closest(JsonDocument? doc, double duration)
    {
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        string? best = null;
        double bestGap = double.MaxValue;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var text = Synced(item);
            if (text is null) continue;
            double d = item.TryGetProperty("duration", out var dv) && dv.ValueKind == JsonValueKind.Number ? dv.GetDouble() : 0;
            double gap = Math.Abs(d - duration);
            if (gap < bestGap) { bestGap = gap; best = text; }
        }
        return best;
    }

    public static async Task<Result> FindAsync(string title, string artist, string album, double duration, CancellationToken ct)
    {
        string? synced = null;
        bool instrumental = false;
        using (var exact = await Get("get", ct, ("track_name", title), ("artist_name", artist), ("album_name", album), ("duration", ((int)Math.Round(duration)).ToString())))
        {
            if (exact is not null)
            {
                synced = Synced(exact.RootElement);
                instrumental = exact.RootElement.TryGetProperty("instrumental", out var i) && i.ValueKind == JsonValueKind.True;
            }
        }
        if (synced is null && !instrumental)
            using (var d = await Get("search", ct, ("track_name", title), ("artist_name", artist))) synced = Closest(d, duration);
        if (synced is null && !instrumental)
            using (var d = await Get("search", ct, ("track_name", Lrc.PlainTitle(title)), ("artist_name", Lrc.MainArtist(artist)))) synced = Closest(d, duration);
        if (synced is null && !instrumental)
            using (var d = await Get("search", ct, ("q", $"{Lrc.PlainTitle(title)} {Lrc.MainArtist(artist)}"))) synced = Closest(d, duration);
        return new Result(synced, instrumental);
    }
}
