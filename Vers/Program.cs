using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using LuneVers;

namespace Vers;

public static class Program
{
    public const string ShowSignal = "LuneVers.Vers.Show";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--snapshot") return Snapshot.Run(args[1]);
        using var mutex = new Mutex(true, "LuneVers.Vers.SingleInstance", out bool first);
        if (!first)
        {
            // Already running: tell that copy to say where it is, instead of quietly doing nothing.
            try { EventWaitHandle.OpenExisting(ShowSignal).Set(); } catch { }
            return 0;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Crash.Install("Vers", app);
        app.Startup += (_, _) => new Shell(app).Start(!args.Contains("--background") && !args.Contains("--preview"), args.Contains("--preview"));
        app.Run();
        return 0;
    }
}

/// Owns the engine, the lyrics windows and the tray menu.
public sealed class Shell
{
    readonly Application app;
    Engine engine = null!;
    readonly List<OverlayWindow> windows = new();
    TaskbarIcon? tray;
    string layoutKey = "";

    public Shell(Application app) { this.app = app; }

    public async void Start(bool greet, bool preview = false)
    {
        try { await StartCore(greet, preview); }
        catch (Exception e) { Crash.Write("Vers", e, true); app.Shutdown(1); }
    }

    /// Vers has no window of its own, so say it's running and where its menu is.
    void Greet()
    {
        var notice = new UpdateNotice();
        notice.Set("Vers is running. Lyrics appear when a song plays. Its menu is the Vers icon by the clock (click ^ if it's hidden), and its settings are also in Lune's player.", -2);
        notice.Show();
        _ = Task.Delay(7000).ContinueWith(_ => notice.Dispatcher.BeginInvoke(notice.Close));
    }

    async Task StartCore(bool greet, bool preview)
    {
        engine = new Engine(app.Dispatcher);
        if (preview) engine.StartPreview();
        engine.Changed += Layout;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => app.Dispatcher.BeginInvoke(() => { layoutKey = ""; Layout(); });
        BuildTray();
        Layout();
        Updater.Schedule("Vers", app.Dispatcher, () => { tray?.Dispose(); app.Shutdown(); });
        Updater.AnnounceIfUpdated("Vers", engine.Settings.LastVersion, v => { engine.Settings.LastVersion = v; engine.Update(); });
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowSignal);
        new Thread(() => { while (signal.WaitOne()) app.Dispatcher.BeginInvoke(Greet); }) { IsBackground = true }.Start();
        if (greet) Greet();
        await engine.Media.StartAsync();
    }

    void Layout()
    {
        var s = engine.Settings;
        var all = Native.Monitors();
        var chosen = s.Display == "all" ? all : all.Where(m => m.Device == s.Display).ToList();
        if (chosen.Count == 0) chosen = all.Take(1).ToList();
        var key = string.Join(";", chosen.Select(m => $"{m.Device}{m.Bounds.Left},{m.Bounds.Top},{m.Bounds.Width},{m.Bounds.Height}")) + $"|{s.BlackBackground}|{s.AboveWindows}";
        if (key == layoutKey) return;
        layoutKey = key;
        foreach (var w in windows) w.Close();
        windows.Clear();
        foreach (var m in chosen)
        {
            var w = new OverlayWindow(engine, s.BlackBackground);
            w.Place(m);
            // The black window shows itself only while the black is on.
            if (!s.BlackBackground) { w.Show(); w.Reposition(); }
            windows.Add(w);
        }
    }

    void BuildTray()
    {
        tray = new TaskbarIcon { ToolTipText = "Vers" };
        try { tray.IconSource = new BitmapImage(new Uri("pack://application:,,,/Vers;component/app.ico")); } catch { }
        var menu = new ContextMenu();
        menu.Opened += (_, _) => FillMenu(menu);
        tray.ContextMenu = menu;
        tray.TrayLeftMouseUp += (_, _) => { FillMenu(menu); menu.IsOpen = true; };
        FillMenu(menu);
    }

    void FillMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        var s = engine.Settings;
        var m = engine.Media;
        MenuItem Info(string text) => new() { Header = text, IsEnabled = false };
        MenuItem Item(string header, Action act, bool? check = null, string? tip = null)
        {
            var i = new MenuItem { Header = header, IsCheckable = check is not null, IsChecked = check ?? false, ToolTip = tip };
            i.Click += (_, _) => act();
            return i;
        }
        menu.Items.Add(new MenuItem { Header = m.HasTrack ? m.Title : "Nothing playing", IsEnabled = false, FontWeight = FontWeights.SemiBold });
        if (m.HasTrack) menu.Items.Add(Info(m.Source.Length > 0 ? $"{m.Artist} · {m.Source}" : m.Artist));
        menu.Items.Add(Info(engine.Status));
        menu.Items.Add(new Separator());

        var style = new MenuItem { Header = "Style" };
        style.Items.Add(Item(s.AutoStyle && engine.AutoPick is { } pick ? $"Auto · {StyleInfo.Name(pick)}" : "Auto (matches each song)", () => { s.AutoStyle = !s.AutoStyle; engine.Update(); }, s.AutoStyle,
            "Picks a style to suit each song: fast lyrics get punchy styles, slow songs the airy ones"));
        foreach (var st in Enum.GetValues<LyricStyle>())
            style.Items.Add(Item(StyleInfo.Name(st), () => { s.Style = st; s.AutoStyle = false; engine.Update(); }, !s.AutoStyle && s.Style == st, StyleInfo.Summary(st)));
        menu.Items.Add(style);

        var font = new MenuItem { Header = "Font" };
        foreach (var f in VersSettings.Fonts)
        {
            var i = Item(LuneVers.Fonts.Label(f), () => { s.Font = f; engine.Update(); }, s.Font == f, LuneVers.Fonts.Detail(f));
            i.FontFamily = LuneVers.Fonts.Family(f);
            font.Items.Add(i);
        }
        menu.Items.Add(font);

        var size = new MenuItem { Header = $"Text Size ({s.Size * 100:0}%)" };
        size.Items.Add(Item("Larger", () => { s.Size = Math.Min(1.6, Math.Round(s.Size + 0.1, 2)); engine.Update(); }));
        size.Items.Add(Item("Smaller", () => { s.Size = Math.Max(0.6, Math.Round(s.Size - 0.1, 2)); engine.Update(); }));
        size.Items.Add(Item("Reset", () => { s.Size = 1; engine.Update(); }));
        menu.Items.Add(size);

        var display = new MenuItem { Header = "Display" };
        var monitors = Native.Monitors();
        for (int k = 0; k < monitors.Count; k++)
        {
            var mon = monitors[k];
            int index = k;
            bool selected = s.Display == mon.Device || (k == 0 && s.Display == "main");
            display.Items.Add(Item(k == 0 ? $"{mon.Name} (main)" : mon.Name, () => { s.Display = index == 0 ? "main" : mon.Device; engine.Update(); }, selected));
        }
        if (monitors.Count > 1) display.Items.Add(Item("All Displays", () => { s.Display = "all"; engine.Update(); }, s.Display == "all"));
        menu.Items.Add(display);

        var color = new MenuItem { Header = "Color" };
        color.Items.Add(Item("Auto Sync (album colors)", () => { s.AutoColor = !s.AutoColor; engine.Update(); }, s.AutoColor));
        color.Items.Add(new Separator());
        foreach (var (name, lyric, highlight, secondary) in new[]
        {
            ("Rose", "#F4F4F8", "#FF4D8A", "#9AA0B4"), ("Ocean", "#EAF4FF", "#3DA9FF", "#8FA6BF"), ("Lime", "#F2FFF0", "#7CFF4D", "#9DB39A"),
            ("Amber", "#FFF6EA", "#FFB13D", "#BFA88F"), ("Violet", "#F6F0FF", "#A66BFF", "#A89ABF"), ("Mono", "#FFFFFF", "#FFFFFF", "#8A8A8A"),
        })
            color.Items.Add(Item(name, () => { s.Lyric = lyric; s.Highlight = highlight; s.Secondary = secondary; s.AutoColor = false; engine.Update(); },
                !s.AutoColor && s.Highlight == highlight && s.Lyric == lyric));
        menu.Items.Add(color);
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Show Lyrics", () => { s.Visible = !s.Visible; engine.Update(); }, s.Visible));
        menu.Items.Add(Item("Keep Screen Awake", () => { s.KeepAwake = !s.KeepAwake; engine.Update(); }, s.KeepAwake));
        menu.Items.Add(Item("Only While Playing", () => { s.OnlyWhilePlaying = !s.OnlyWhilePlaying; engine.Update(); }, s.OnlyWhilePlaying));
        menu.Items.Add(Item("Black Background", () => { s.BlackBackground = !s.BlackBackground; engine.Update(); }, s.BlackBackground));
        menu.Items.Add(Item("Show Above Windows", () => { s.AboveWindows = !s.AboveWindows; engine.Update(); }, s.AboveWindows, "Off keeps the lyrics on the desktop, behind your windows"));
        var timing = new MenuItem { Header = $"Timing ({s.Offset:+0.0;-0.0;0.0}s)" };
        foreach (var (label, delta) in new[] { ("Show Earlier  +0.1s", 0.1), ("Show Earlier  +0.5s", 0.5), ("Show Later  −0.1s", -0.1), ("Show Later  −0.5s", -0.5) })
            timing.Items.Add(Item(label, () => { s.Offset = Math.Clamp(Math.Round(s.Offset + delta, 1), -5, 5); engine.Update(); }));
        timing.Items.Add(new Separator());
        timing.Items.Add(Item("Reset Timing", () => { s.Offset = 0; engine.Update(); }));
        menu.Items.Add(timing);
        menu.Items.Add(new Separator());

        if (m.HasTrack) menu.Items.Add(Item(m.Playing ? "Pause" : "Play", m.PlayPause));
        menu.Items.Add(Item("Retry Lyrics", engine.Retry));
        menu.Items.Add(Item("Import Lyrics (.lrc)…", ImportLyrics));
        menu.Items.Add(Item("Start with Windows", () => Startup.Set("Vers", !Startup.IsOn("Vers")), Startup.IsOn("Vers")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item($"Check for Updates (version {Updater.Current.ToString(2)})", () => _ = Updater.Check("Vers", app.Dispatcher, () => { tray?.Dispose(); app.Shutdown(); }, true)));
        menu.Items.Add(Item("Quit Vers", () => { tray?.Dispose(); app.Shutdown(); }));
    }

    void ImportLyrics()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Lyrics (*.lrc;*.txt)|*.lrc;*.txt|All files|*.*", Title = "Import lyrics" };
        if (dialog.ShowDialog() == true)
        {
            try { engine.Import(File.ReadAllText(dialog.FileName), Path.GetFileName(dialog.FileName)); } catch { }
        }
    }
}

/// `Vers.exe --snapshot <folder>` draws each style to a PNG for the build to check.
public static class Snapshot
{
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int code = 0;
        app.Startup += (_, _) =>
        {
            try
            {
                var engine = new Engine(app.Dispatcher);
                engine.Import(Demo.Lyrics, "demo.lrc");
                foreach (var style in Enum.GetValues<LyricStyle>())
                {
                    var canvas = new LyricsCanvas(engine) { FixedStyle = style, FixedTime = 13.4, FixedPresence = 1 };
                    var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(18, 20, 30)) };
                    root.Children.Add(canvas);
                    root.Measure(new Size(1280, 720));
                    root.Arrange(new Rect(0, 0, 1280, 720));
                    root.UpdateLayout();
                    var rtb = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(rtb));
                    using var file = File.Create(Path.Combine(folder, $"vers-{style.ToString().ToLowerInvariant()}.png"));
                    encoder.Save(file);
                }
                Console.WriteLine("snapshots written");
            }
            catch (Exception e) { Console.Error.WriteLine(e); code = 1; }
            app.Shutdown(code);
        };
        app.Run();
        return code;
    }
}
