using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace LuneVers;

/// Keeps the app up to date from the website's GitHub releases: finds a newer
/// "windows-x.y" release, downloads this app's installer, checks it against
/// the checksum GitHub publishes, and installs it quietly. The installer
/// reopens the app when it's done.
public static class Updater
{
    const string Releases = "https://api.github.com/repos/MrRobloxYT/lune-vers/releases?per_page=10";

    public static Version Current => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0);

    /// Returns true when an update is installing and the app should quit.
    public static async Task<bool> UpdateAsync(string app, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LuneVers-Updater/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Releases, ct));
            Version best = Current;
            string? url = null, digest = null;
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
                var tag = release.GetProperty("tag_name").GetString() ?? "";
                if (!tag.StartsWith("windows-") || !Version.TryParse(tag["windows-".Length..], out var v)) continue;
                v = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
                if (v <= Normal(best)) continue;
                foreach (var asset in release.GetProperty("assets").EnumerateArray())
                {
                    if (asset.GetProperty("name").GetString() != $"{app}-Setup.exe") continue;
                    best = v;
                    url = asset.GetProperty("browser_download_url").GetString();
                    digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                }
            }
            if (url is null || digest is null || !digest.StartsWith("sha256:")) return false;
            var bytes = await http.GetByteArrayAsync(url, ct);
            // Only run a file that matches the checksum GitHub recorded for it.
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(digest["sha256:".Length..], StringComparison.OrdinalIgnoreCase)) return false;
            var path = Path.Combine(Path.GetTempPath(), $"{app}-Setup-{best}.exe");
            await File.WriteAllBytesAsync(path, bytes, ct);
            Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    static Version Normal(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// Checks shortly after launch, then every six hours.
    public static void Schedule(string app, System.Windows.Threading.Dispatcher ui, Action quit)
    {
        async void Check()
        {
            if (await UpdateAsync(app)) ui.Invoke(quit);
        }
        var timer = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromHours(6), System.Windows.Threading.DispatcherPriority.Background, (_, _) => Check(), ui);
        timer.Start();
        _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => ui.BeginInvoke(Check));
    }
}
