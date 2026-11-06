using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LuneVers;

/// Keeps the app up to date from the website's GitHub releases: finds a newer
/// "windows-x.y" release, downloads this app's installer while showing its
/// progress, checks it against the checksum GitHub publishes, and installs
/// it quietly. The installer reopens the app when it's done.
public static class Updater
{
    const string Releases = "https://api.github.com/repos/MrRobloxYT/lune-vers/releases?per_page=10";
    public enum Result { UpToDate, Installing, Failed }
    static bool busy;

    public static Version Current => Normal(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0));
    static Version Normal(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static async Task<Result> UpdateAsync(string app, Action<string, double>? progress = null, CancellationToken ct = default)
    {
        if (busy) return Result.Failed;
        busy = true;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
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
                v = Normal(v);
                if (v <= best) continue;
                foreach (var asset in release.GetProperty("assets").EnumerateArray())
                {
                    if (asset.GetProperty("name").GetString() != $"{app}-Setup.exe") continue;
                    best = v;
                    url = asset.GetProperty("browser_download_url").GetString();
                    digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                }
            }
            if (url is null) return Result.UpToDate;
            if (digest is null || !digest.StartsWith("sha256:")) return Result.Failed;
            string label = $"Updating {app} to {best.ToString(2)}…";
            progress?.Invoke(label, 0);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read; long done = 0; double last = 0;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                memory.Write(buffer, 0, read); done += read;
                double pct = total > 0 ? (double)done / total : 0;
                if (pct - last > 0.01) { last = pct; progress?.Invoke(label, pct); }
            }
            var bytes = memory.ToArray();
            // Only run a file that matches the checksum GitHub recorded for it.
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(digest["sha256:".Length..], StringComparison.OrdinalIgnoreCase)) return Result.Failed;
            var path = Path.Combine(Path.GetTempPath(), $"{app}-Setup-{best}.exe");
            await File.WriteAllBytesAsync(path, bytes, ct);
            progress?.Invoke($"Installing {app} {best.ToString(2)}. It reopens in a moment.", 1);
            await Task.Delay(1200, ct);
            Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
            return Result.Installing;
        }
        catch { return Result.Failed; }
        finally { busy = false; }
    }

    /// Checks shortly after launch, then every six hours, showing a small
    /// notice with a progress bar while an update downloads.
    public static void Schedule(string app, System.Windows.Threading.Dispatcher ui, Action quit)
    {
        var timer = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromHours(6), System.Windows.Threading.DispatcherPriority.Background, (_, _) => _ = Check(app, ui, quit, false), ui);
        timer.Start();
        _ = Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith(_ => ui.BeginInvoke(() => _ = Check(app, ui, quit, false)));
    }

    /// `manual`: also say so when there's nothing new or the check failed.
    public static async Task<Result> Check(string app, System.Windows.Threading.Dispatcher ui, Action quit, bool manual)
    {
        UpdateNotice? notice = null;
        if (manual) { notice = new UpdateNotice(); notice.Set($"Checking for {app} updates…", -1); notice.Show(); }
        var result = await UpdateAsync(app, (text, pct) => ui.BeginInvoke(() =>
        {
            if (notice is null) { notice = new UpdateNotice(); notice.Show(); }
            notice.Set(text, pct);
        }));
        if (result == Result.Installing) { quit(); return result; }
        if (notice is not null)
        {
            notice.Set(result == Result.UpToDate ? $"{app} {Current.ToString(2)} is up to date" : "Couldn't check for updates. Try again later.", -2);
            await Task.Delay(2600);
            notice.Close();
        }
        return result;
    }

    /// After an update installed: shows "updated" once.
    public static void AnnounceIfUpdated(string app, string lastVersion, Action<string> remember)
    {
        var now = Current.ToString(3);
        if (lastVersion == now) return;
        remember(now);
        if (lastVersion.Length == 0) return;
        var notice = new UpdateNotice();
        notice.Set($"{app} updated to {Current.ToString(2)}", -2);
        notice.Show();
        _ = Task.Delay(3500).ContinueWith(_ => notice.Dispatcher.BeginInvoke(notice.Close));
    }
}

/// A small frosted card in the bottom-right corner with a progress bar.
public sealed class UpdateNotice : Window
{
    readonly TextBlock text = new() { Foreground = Brushes.White, FontSize = 13, FontFamily = Fonts.Inter, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Medium };
    readonly Border track = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Margin = new Thickness(0, 10, 0, 0) };
    readonly Border bar = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0,
        Background = new LinearGradientBrush(Color.FromRgb(255, 77, 138), Color.FromRgb(61, 120, 255), 0) };

    public UpdateNotice()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Width = 320; SizeToContent = SizeToContent.Height;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        track.Child = bar;
        var stack = new StackPanel();
        stack.Children.Add(text);
        stack.Children.Add(track);
        Content = new Border
        {
            Child = stack, Padding = new Thickness(18, 14, 18, 16), CornerRadius = new CornerRadius(16), Margin = new Thickness(14),
            Background = new SolidColorBrush(Color.FromArgb(235, 24, 22, 30)),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(80, 255, 255, 255), Color.FromArgb(14, 255, 255, 255), 90), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.4 },
        };
        Opacity = 0;
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - 8; Top = work.Bottom - ActualHeight - 8;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        };
        SizeChanged += (_, _) => { var work = SystemParameters.WorkArea; Top = work.Bottom - ActualHeight - 8; };
    }

    /// `progress` 0…1 fills the bar; -1 runs it as "working"; -2 hides it.
    public void Set(string message, double progress)
    {
        text.Text = message;
        track.Visibility = progress <= -2 ? Visibility.Collapsed : Visibility.Visible;
        double width = 284;
        if (progress >= 0) bar.BeginAnimation(WidthProperty, new DoubleAnimation(width * Math.Clamp(progress, 0, 1), TimeSpan.FromMilliseconds(200)));
        else if (progress == -1) bar.BeginAnimation(WidthProperty, new DoubleAnimation(0, width, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true });
    }
}
