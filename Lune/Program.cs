using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using LuneVers;

namespace Lune;

public static class Program
{
    const string OpenSignal = "LuneVers.Lune.OpenPlayer";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--snapshot") return Snapshot.Run(args[1]);
        using var mutex = new Mutex(true, "LuneVers.Lune.SingleInstance", out bool first);
        if (!first)
        {
            // Already running: opening Lune again brings up the player, like the Dock icon on a Mac.
            try { EventWaitHandle.OpenExisting(OpenSignal).Set(); } catch { }
            return 0;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) => new Shell(app, OpenSignal, args).Start();
        app.Run();
        return 0;
    }
}

/// Owns the model, the light, the player and the tray icon.
public sealed class Shell
{
    readonly Application app;
    readonly string signalName;
    readonly string[] args;
    Model model = null!;
    EdgeLight light = null!;
    PlayerWindow? player;
    TaskbarIcon? tray;

    public Shell(Application app, string signalName, string[] args) { this.app = app; this.signalName = signalName; this.args = args; }

    public async void Start()
    {
        model = new Model(app.Dispatcher);
        light = new EdgeLight(model);
        BuildTray();
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, signalName);
        var thread = new Thread(() => { while (signal.WaitOne()) app.Dispatcher.BeginInvoke(ShowPlayer); }) { IsBackground = true };
        thread.Start();
        Updater.Schedule("Lune", app.Dispatcher, () => { tray?.Dispose(); app.Shutdown(); });
        await model.Media.StartAsync();
        if (!model.Settings.Welcomed || !args.Contains("--background"))
        {
            model.Settings.Welcomed = true; model.Update();
            ShowPlayer();
        }
    }

    void ShowPlayer()
    {
        player ??= new PlayerWindow(model);
        player.Show();
        if (player.WindowState == WindowState.Minimized) player.WindowState = WindowState.Normal;
        player.Activate();
    }

    void BuildTray()
    {
        tray = new TaskbarIcon { ToolTipText = "Lune" };
        try { tray.IconSource = new BitmapImage(new Uri("pack://application:,,,/Lune;component/app.ico")); } catch { }
        tray.TrayLeftMouseUp += (_, _) => { if (player is { IsVisible: true }) player.Hide(); else ShowPlayer(); };
        var menu = new ContextMenu();
        menu.Opened += (_, _) => FillMenu(menu);
        tray.ContextMenu = menu;
        FillMenu(menu);
    }

    void FillMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        var s = model.Settings;
        var m = model.Media;
        menu.Items.Add(new MenuItem { Header = m.HasTrack ? $"{m.Title} — {m.Artist}" : "Nothing playing", IsEnabled = false });
        menu.Items.Add(new Separator());
        MenuItem Item(string header, Action act, bool? check = null)
        {
            var i = new MenuItem { Header = header, IsCheckable = check is not null, IsChecked = check ?? false };
            i.Click += (_, _) => act();
            return i;
        }
        menu.Items.Add(Item("Open Player", ShowPlayer));
        menu.Items.Add(Item("Light On", () => { s.Enabled = !s.Enabled; model.Update(); }, s.Enabled));
        var motion = new MenuItem { Header = "Animation" };
        foreach (var (mode, name) in new[] { (Motion.Default, "Default"), (Motion.MusicSync, "Music Sync"), (Motion.Idle, "Idle"), (Motion.None, "No Animation") })
            motion.Items.Add(Item(name, () => { s.Motion = mode; model.Update(); }, s.Motion == mode));
        menu.Items.Add(motion);
        menu.Items.Add(Item("Only While Playing", () => { s.OnlyWhilePlaying = !s.OnlyWhilePlaying; model.Update(); }, s.OnlyWhilePlaying));
        menu.Items.Add(Item("Start with Windows", () => Startup.Set("Lune", !Startup.IsOn("Lune")), Startup.IsOn("Lune")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item($"Version {Updater.Current.ToString(2)} · updates itself", () => { }));
        menu.Items.Add(Item("Quit Lune", () => { tray?.Dispose(); app.Shutdown(); }));
    }
}

/// `Lune.exe --snapshot <folder>` draws the light and the player to PNGs, so
/// the build can check them on a machine with no music playing.
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
                var s = new LuneSettings();
                var colors = new List<Rgb> { new(1, 0.25f, 0.45f), new(0.25f, 0.45f, 1f), new(0.2f, 0.85f, 0.5f) };
                foreach (var (mode, name) in new[] { (Motion.Default, "default"), (Motion.MusicSync, "musicsync"), (Motion.Idle, "idle"), (Motion.None, "none") })
                {
                    var glow = EdgeLight.Snapshot(s, colors.Take(2).ToList(), 1280, 800, 3.2f, mode);
                    Save(OnBlack(glow), Path.Combine(folder, $"glow-{name}.png"));
                }
                var model = new Model(app.Dispatcher);
                var art = DemoArt();
                var doc = Lrc.Parse(Demo.Lyrics, 120);
                foreach (var (lyricsView, settings, name) in new[] { (false, false, "player"), (true, false, "player-lyrics"), (false, true, "player-settings") })
                {
                    var window = new PlayerWindow(model);
                    window.Demo("Neon Rain", "Lune Demo · Spotify", art, doc, 14.2, lyricsView, settings);
                    var surface = window.Surface;
                    window.Content = null;
                    surface.Measure(new Size(1010, 705));
                    surface.Arrange(new Rect(0, 0, 1010, 705));
                    surface.UpdateLayout();
                    var rtb = new RenderTargetBitmap(1010, 705, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(surface);
                    Save(rtb, Path.Combine(folder, $"{name}.png"));
                }
                Console.WriteLine("snapshots written");
            }
            catch (Exception e) { Console.Error.WriteLine(e); code = 1; }
            app.Shutdown(code);
        };
        app.Run();
        return code;
    }

    static BitmapSource OnBlack(BitmapSource image)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 12, 16)), null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        }
        var rtb = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return rtb;
    }

    static BitmapSource DemoArt()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(255, 70, 120), Color.FromRgb(60, 90, 255), 45), null, new Rect(0, 0, 300, 300));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(150, 255, 210, 90)), null, new Point(200, 110), 60, 60);
        }
        var rtb = new RenderTargetBitmap(300, 300, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
