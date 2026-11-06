using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using LuneVers;

namespace Lune;

/// The player: frosted glass over the desktop, the album cover that grows
/// under the pointer, lyrics that fill in word by word, and the light's
/// controls along the bottom. Everything scales with the window.
public sealed class PlayerWindow : Window
{
    const double BaseW = 1010, BaseH = 705;
    readonly Model model;
    readonly PlayerLyrics lyrics;
    readonly Grid outer = new();
    readonly Grid stage = new() { Width = BaseW, Height = BaseH };
    readonly RadialGradientBrush glow = new() { RadiusX = 0.55, RadiusY = 0.6, Center = new Point(0.5, 0.42), GradientOrigin = new Point(0.5, 0.42) };
    readonly Rectangle glowLayer = new() { IsHitTestVisible = false };
    readonly Border fallbackBackground = new() { Background = new SolidColorBrush(Color.FromArgb(244, 22, 20, 26)), Visibility = Visibility.Collapsed };

    // Main view
    readonly Grid main = new();
    readonly Grid coverHolder = new() { Width = 270, Height = 270, RenderTransformOrigin = new Point(0.5, 0.5) };
    readonly ScaleTransform coverScale = new(1, 1);
    readonly Border coverFrame;
    readonly Border coverBox = new() { CornerRadius = new CornerRadius(18), Width = 250, Height = 250 };
    readonly Image cover = new() { Stretch = Stretch.UniformToFill };
    readonly TextBlock title = Ui.Label("Nothing playing", 22, FontWeights.SemiBold);
    readonly TextBlock artist = Ui.Label("Play a song in Spotify, Apple Music or a browser", 15, null, 0.6);
    readonly TextBlock playGlyph = Ui.Icon(Ui.Glyph.Play, 22);
    readonly Pill lyricsButton;

    // Lyrics view
    readonly Border lyricsCard;
    readonly Border smallCoverBox = new() { CornerRadius = new CornerRadius(14), Width = 158, Height = 158 };
    readonly Image smallCover = new() { Stretch = Stretch.UniformToFill };
    readonly TextBlock smallTitle = Ui.Label("", 15, FontWeights.SemiBold);
    readonly TextBlock smallArtist = Ui.Label("", 12.5, null, 0.6);
    readonly TextBlock smallPlayGlyph = Ui.Icon(Ui.Glyph.Play, 16);
    readonly TextBlock offsetLabel = Ui.Label("In sync", 11.5, FontWeights.SemiBold, 0.6);

    // Bottom bar and settings
    readonly StackPanel bar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 26) };
    readonly Dictionary<string, (Pill Button, StackPanel Group)> groups = new();
    string? openGroup;
    readonly Border settingsPanel;
    readonly Pill settingsButton;
    readonly Pill pinButton;
    readonly List<Action> refreshers = new();

    bool showLyrics, hoverCover, fullscreen, glass;
    Rect restoreBounds;
    double pulse, lastTick;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    public PlayerWindow(Model model)
    {
        this.model = model;
        lyrics = new PlayerLyrics(model);
        Title = "Lune";
        Width = 1010; Height = 705; MinWidth = 640; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        Foreground = Brushes.White;
        FontFamily = Ui.Text;
        // Grayscale text: ClearType draws colored fringes on glass.
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        UseLayoutRounding = true;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Lune;component/app.ico")); } catch { }
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
        });
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = Colors.Transparent;
            glass = Native.Acrylic(hwnd);
            // Windows 10 has no frosted glass behind windows: use a dark tint instead.
            if (!glass) fallbackBackground.Visibility = Visibility.Visible;
        };

        coverFrame = Ui.Glass(coverBox, 26, 0.08, 10);
        lyricsButton = Pill.Text("Lyrics", 40, Ui.Glyph.Lyrics, 14);
        lyricsCard = BuildLyricsCard();
        settingsButton = Pill.Text("Settings", 46, null, 15);
        pinButton = Pill.Round(Ui.Glyph.Pin, 46, 17);
        settingsPanel = BuildSettings();

        outer.Children.Add(fallbackBackground);
        outer.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(46, 13, 11, 13)), IsHitTestVisible = false });
        glowLayer.Fill = glow;
        outer.Children.Add(glowLayer);
        outer.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = stage });
        outer.Children.Add(BuildCaption());
        Content = outer;

        BuildMain();
        stage.Children.Add(main);
        stage.Children.Add(lyricsCard);
        BuildBar();
        stage.Children.Add(bar);
        stage.Children.Add(settingsPanel);
        lyricsCard.Visibility = Visibility.Collapsed; lyricsCard.Opacity = 0;
        settingsPanel.Visibility = Visibility.Collapsed; settingsPanel.Opacity = 0;

        outer.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { ToggleFullScreen(); return; }
            if (!fullscreen && e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11) ToggleFullScreen();
            else if (e.Key == Key.Escape && fullscreen) ToggleFullScreen();
            else if (e.Key == Key.Space) model.Media.PlayPause();
        };
        Closing += (_, e) => { e.Cancel = true; Hide(); };   // closing keeps Lune running in the tray

        model.Media.TrackChanged += UpdateTrack;
        model.Media.StateChanged += UpdateState;
        model.ArtworkChanged += UpdateTrack;
        model.Changed += RefreshControls;
        CompositionTarget.Rendering += OnFrame;
        Topmost = model.Settings.KeepPlayerOnTop;
        UpdateTrack(); UpdateState(); RefreshControls();
    }

    // MARK: Layout

    UIElement BuildCaption()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 12, 0) };
        Pill Btn(string glyph, Action act, string tip) { var p = Pill.Round(glyph, 30, 11, 0.06); p.ToolTip = tip; p.Margin = new Thickness(6, 0, 0, 0); p.Click += act; return p; }
        row.Children.Add(Btn(Ui.Glyph.Minimize, () => WindowState = WindowState.Minimized, "Minimize"));
        var fs = Btn(Ui.Glyph.FullScreen, ToggleFullScreen, "Full screen (F11)");
        row.Children.Add(fs);
        refreshers.Add(() => ((TextBlock)fs.Child).Text = fullscreen ? Ui.Glyph.ExitFullScreen : Ui.Glyph.FullScreen);
        row.Children.Add(Btn(Ui.Glyph.Close, () => Hide(), "Close (Lune keeps running in the tray)"));
        return row;
    }

    void BuildMain()
    {
        coverBox.Child = cover;
        coverBox.Clip = new RectangleGeometry(new Rect(0, 0, 250, 250), 18, 18);
        coverBox.Background = Ui.Fill(0.1);
        coverHolder.RenderTransform = coverScale;
        coverHolder.Children.Add(coverFrame);
        coverFrame.HorizontalAlignment = HorizontalAlignment.Center; coverFrame.VerticalAlignment = VerticalAlignment.Center;
        coverHolder.Background = Brushes.Transparent;
        coverHolder.MouseEnter += (_, _) => SetHover(true);
        coverHolder.MouseLeave += (_, _) => SetHover(false);
        coverHolder.MouseLeftButtonUp += (_, e) => { model.Media.PlayPause(); e.Handled = true; };
        coverHolder.MouseLeftButtonDown += (_, e) => e.Handled = true;
        coverHolder.Cursor = Cursors.Hand;

        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 64, 0, 0) };
        column.Children.Add(coverHolder);
        title.HorizontalAlignment = HorizontalAlignment.Center; title.Margin = new Thickness(0, 34, 0, 0); title.MaxWidth = 520;
        artist.HorizontalAlignment = HorizontalAlignment.Center; artist.Margin = new Thickness(0, 6, 0, 0); artist.MaxWidth = 520;
        column.Children.Add(title);
        column.Children.Add(artist);
        column.Children.Add(Transport(false));
        lyricsButton.HorizontalAlignment = HorizontalAlignment.Center;
        lyricsButton.Margin = new Thickness(0, 22, 0, 0);
        lyricsButton.Click += () => SetLyrics(true);
        column.Children.Add(lyricsButton);
        main.Children.Add(column);
    }

    StackPanel Transport(bool plain)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, plain ? 14 : 24, 0, 0) };
        void Add(Pill p, Action act, string tip, double gap) { p.Click += act; p.ToolTip = tip; p.Margin = new Thickness(gap, 0, gap, 0); p.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(p); }
        double fill = plain ? 0.0 : 0.08;
        Add(Skip(false, plain ? 30 : 36, fill), () => model.Media.Seek(model.Media.Position - 15), "Back 15 seconds", 5);
        Add(Pill.Round(Ui.Glyph.Previous, plain ? 34 : 44, plain ? 13 : 16, fill), model.Media.Previous, "Previous", 5);
        var play = new Pill(plain ? smallPlayGlyph : playGlyph, plain ? 42 : 58, plain ? 42 : 58, true, plain ? 0.1 : 0.14);
        Add(play, model.Media.PlayPause, "Play or pause (Space)", 5);
        Add(Pill.Round(Ui.Glyph.Next, plain ? 34 : 44, plain ? 13 : 16, fill), model.Media.Next, "Next", 5);
        Add(Skip(true, plain ? 30 : 36, fill), () => model.Media.Seek(model.Media.Position + 15), "Forward 15 seconds", 5);
        return row;
    }

    /// A circular arrow with "15" inside, like the Mac's skip buttons.
    static Pill Skip(bool forward, double size, double fill)
    {
        var g = new Grid();
        var arrow = Ui.Icon(Ui.Glyph.Refresh, size * 0.5);
        if (!forward) { arrow.RenderTransformOrigin = new Point(0.5, 0.5); arrow.RenderTransform = new ScaleTransform(-1, 1); }
        g.Children.Add(arrow);
        g.Children.Add(new TextBlock { Text = "15", FontSize = size * 0.21, FontWeight = FontWeights.Bold, FontFamily = Ui.Text, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) });
        return new Pill(g, size, size, true, fill);
    }

    Border BuildLyricsCard()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(254) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 210 };
        smallCoverBox.Child = smallCover;
        smallCoverBox.Clip = new RectangleGeometry(new Rect(0, 0, 158, 158), 14, 14);
        smallCoverBox.Background = Ui.Fill(0.1);
        left.Children.Add(smallCoverBox);
        smallTitle.HorizontalAlignment = HorizontalAlignment.Center; smallTitle.Margin = new Thickness(0, 14, 0, 0); smallTitle.MaxWidth = 200;
        smallArtist.HorizontalAlignment = HorizontalAlignment.Center; smallArtist.Margin = new Thickness(0, 3, 0, 0); smallArtist.MaxWidth = 200;
        left.Children.Add(smallTitle);
        left.Children.Add(smallArtist);
        left.Children.Add(Transport(true));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        var active = Pill.Text("Lyrics", 34, Ui.Glyph.Lyrics, 12.5);
        active.Click += () => SetLyrics(false);
        refreshers.Add(() => active.Tint = model.Accent.Color);
        var retry = Pill.Round(Ui.Glyph.Refresh, 34, 12);
        retry.ToolTip = "Look for the lyrics again";
        retry.Margin = new Thickness(8, 0, 0, 0);
        retry.Click += lyrics.Retry;
        buttons.Children.Add(active); buttons.Children.Add(retry);
        left.Children.Add(buttons);

        // Timing: nudge the lyrics until they match what you hear.
        var timing = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        var later = Pill.Round(Ui.Glyph.Remove, 26, 10); later.ToolTip = "Show lyrics later";
        var earlier = Pill.Round(Ui.Glyph.Add, 26, 10); earlier.ToolTip = "Show lyrics earlier";
        later.Click += () => Nudge(-0.05); earlier.Click += () => Nudge(0.05);
        offsetLabel.Width = 70; offsetLabel.TextAlignment = TextAlignment.Center; offsetLabel.VerticalAlignment = VerticalAlignment.Center;
        offsetLabel.Cursor = Cursors.Hand; offsetLabel.ToolTip = "Lyrics timing. Click to reset";
        offsetLabel.MouseLeftButtonUp += (_, e) => { Nudge(-model.Settings.LyricsOffset); e.Handled = true; };
        timing.Children.Add(later); timing.Children.Add(offsetLabel); timing.Children.Add(earlier);
        left.Children.Add(timing);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var divider = new Rectangle { Fill = Ui.Fill(0.06), Margin = new Thickness(0, 26, 0, 26) };
        Grid.SetColumn(divider, 1);
        grid.Children.Add(divider);

        var list = new LyricLinesView(lyrics) { Margin = new Thickness(30, 18, 24, 18) };
        Grid.SetColumn(list, 2);
        grid.Children.Add(list);

        var card = Ui.Glass(grid, 28, 0.07);
        card.Width = 640; card.Height = 390;
        card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Top;
        card.Margin = new Thickness(0, 96, 0, 0);
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = new ScaleTransform(0.96, 0.96);
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return card;
    }

    void Nudge(double by)
    {
        var s = model.Settings;
        s.LyricsOffset = Math.Clamp(Math.Round((s.LyricsOffset + by) * 100) / 100, -3, 3);
        model.Update();
    }

    void BuildBar()
    {
        StackPanel Group(string key, Pill button, IEnumerable<UIElement> items)
        {
            var g = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Opacity = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            foreach (var i in items) { if (i is FrameworkElement fe) fe.Margin = new Thickness(4, 0, 4, 0); g.Children.Add(i); }
            groups[key] = (button, g);
            button.Click += () => Open(openGroup == key ? null : key);
            return g;
        }
        void Add(UIElement e) { if (e is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(5, 0, 5, 0); bar.Children.Add(e); }

        // Colors: how many of the album's colors the light uses.
        var palette = Pill.Round(Ui.Glyph.Palette, 46, 17); palette.ToolTip = "Colors";
        Add(palette);
        var counts = new List<UIElement>();
        foreach (var n in new[] { 1, 2, 3 })
        {
            var p = Pill.Text(n == 1 ? "1 color" : $"{n} colors", 36, null, 12.5);
            p.Click += () => { model.Settings.ColorCount = n; model.Update(); };
            refreshers.Add(() => p.Tint = model.Settings.ColorCount == n ? model.Accent.Color : null);
            counts.Add(p);
        }
        Add(Group("palette", palette, counts));

        // Motion modes.
        var motion = Pill.Round(Ui.Glyph.Wave, 46, 17); motion.ToolTip = "Animation";
        Add(motion);
        var modes = new List<UIElement>();
        foreach (var (m, name) in new[] { (Motion.Default, "Default"), (Motion.MusicSync, "Music Sync"), (Motion.Idle, "Idle"), (Motion.None, "No Animation") })
        {
            var p = Pill.Text(name, 36, null, 12.5);
            p.Click += () => { model.Settings.Motion = m; model.Update(); };
            refreshers.Add(() => p.Tint = model.Settings.Motion == m ? model.Accent.Color : null);
            modes.Add(p);
        }
        Add(Group("motion", motion, modes));

        // Brightness of the glow, and the light on or off.
        var light = Pill.Round(Ui.Glyph.Light, 46, 17); light.ToolTip = "Glow";
        Add(light);
        var minus = Pill.Round(Ui.Glyph.Remove, 32, 11);
        var plus = Pill.Round(Ui.Glyph.Add, 32, 11);
        var slider = new GlassSlider(model.Settings.Glow, 150) { VerticalAlignment = VerticalAlignment.Center };
        slider.Changed += v => { model.Settings.Glow = v; Preview(); model.Update(); };
        minus.Click += () => { model.Settings.Glow = Math.Max(0, model.Settings.Glow - 0.1); slider.Value = model.Settings.Glow; Preview(); model.Update(); };
        plus.Click += () => { model.Settings.Glow = Math.Min(1, model.Settings.Glow + 0.1); slider.Value = model.Settings.Glow; Preview(); model.Update(); };
        var onOff = Pill.Text("On", 36, null, 12.5);
        onOff.Click += () => { model.Settings.Enabled = !model.Settings.Enabled; model.Update(); };
        refreshers.Add(() => { ((TextBlock)((StackPanel)onOff.Child).Children[^1]).Text = model.Settings.Enabled ? "On" : "Off"; onOff.Tint = model.Settings.Enabled ? model.Accent.Color : null; slider.Value = model.Settings.Glow; });
        Add(Group("light", light, new UIElement[] { minus, slider, plus, onOff }));

        settingsButton.Click += () => { Open(null); ShowSettings(settingsPanel.Visibility != Visibility.Visible); };
        Add(settingsButton);

        // Which displays light up.
        var display = Pill.Round(Ui.Glyph.Display, 46, 17); display.ToolTip = "Displays";
        Add(display);
        var displayGroup = Group("display", display, Array.Empty<UIElement>());
        display.Click += () => FillDisplays(displayGroup);
        Add(displayGroup);

        pinButton.ToolTip = "Keep the player on top";
        pinButton.Click += () => { model.Settings.KeepPlayerOnTop = !model.Settings.KeepPlayerOnTop; Topmost = model.Settings.KeepPlayerOnTop; model.Update(); };
        refreshers.Add(() => { pinButton.Tint = model.Settings.KeepPlayerOnTop ? model.Accent.Color : null; ((TextBlock)pinButton.Child).Text = model.Settings.KeepPlayerOnTop ? Ui.Glyph.Unpin : Ui.Glyph.Pin; });
        Add(pinButton);
    }

    void FillDisplays(StackPanel group)
    {
        group.Children.Clear();
        var monitors = Native.Monitors();
        var items = new List<(string Key, string Name)> { ("main", "Main display") };
        foreach (var m in monitors.Skip(1)) items.Add((m.Device, m.Name));
        if (monitors.Count > 1) items.Add(("all", "All displays"));
        foreach (var (key, name) in items)
        {
            var p = Pill.Text(name, 36, null, 12.5);
            p.Margin = new Thickness(4, 0, 4, 0);
            bool selected = model.Settings.Display == key || (key == "main" && monitors.Count > 0 && model.Settings.Display == monitors[0].Device);
            p.Tint = selected ? model.Accent.Color : null;
            p.Click += () => { model.Settings.Display = key; model.Update(); FillDisplays(group); };
            group.Children.Add(p);
        }
    }

    void Open(string? key)
    {
        openGroup = key;
        foreach (var (k, (button, group)) in groups)
        {
            Ui.Show(group, k == key, 200);
            button.Active = k == key;
        }
    }

    Border BuildSettings()
    {
        var stack = new StackPanel { Width = 400 };
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(Ui.Label("Settings", 19, FontWeights.SemiBold));
        var close = Pill.Round(Ui.Glyph.Close, 28, 10, 0.06);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Click += () => ShowSettings(false);
        head.Children.Add(close);
        stack.Children.Add(head);

        void Row(string label, FrameworkElement control, string? hint = null)
        {
            var g = new Grid { Margin = new Thickness(0, 7, 0, 7) };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 150, 0) };
            text.Children.Add(Ui.Label(label, 13.5, FontWeights.Medium, 0.92));
            if (hint is not null) { var h = Ui.Label(hint, 11, null, 0.5); h.TextWrapping = TextWrapping.Wrap; h.TextTrimming = TextTrimming.None; text.Children.Add(h); }
            g.Children.Add(text);
            control.HorizontalAlignment = HorizontalAlignment.Right; control.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(control);
            stack.Children.Add(g);
        }
        GlassToggle Toggle(Func<bool> get, Action<bool> set)
        {
            var t = new GlassToggle(get());
            t.Changed += v => { set(v); Preview(); model.Update(); };
            refreshers.Add(() => { if (t.On != get()) t.On = get(); t.Accent = model.Accent.Color; });
            return t;
        }

        var s = model.Settings;
        Row("Light", Toggle(() => s.Enabled, v => s.Enabled = v), "Lights the edges of your screen");
        Row("Only while music plays", Toggle(() => s.OnlyWhilePlaying, v => s.OnlyWhilePlaying = v), "Fades out when you pause");
        Row("Colors from the album", Toggle(() => !s.OverrideAlbum, v => s.OverrideAlbum = !v), "Off uses your own colors below");

        string[] swatches = { "#FF1A4D", "#FF6A1A", "#FFC21A", "#26D95C", "#1AD9C8", "#1A6BFF", "#7A3DFF", "#E040FB", "#FFFFFF" };
        void Swatches(string label, Func<string> get, Action<string> set)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var hex in swatches)
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(color), Margin = new Thickness(3, 0, 0, 0), BorderThickness = new Thickness(2), Cursor = Cursors.Hand };
                dot.MouseLeftButtonUp += (_, e) => { set(hex); s.OverrideAlbum = true; Preview(); model.Update(); e.Handled = true; };
                refreshers.Add(() => dot.BorderBrush = string.Equals(get(), hex, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent);
                row.Children.Add(dot);
            }
            Row(label, row);
        }
        Swatches("Primary", () => s.Primary, v => s.Primary = v);
        Swatches("Secondary", () => s.Secondary, v => s.Secondary = v);
        Swatches("Tertiary", () => s.Tertiary, v => s.Tertiary = v);

        var thickness = new GlassSlider(s.Thickness, 140);
        thickness.Changed += v => { s.Thickness = v; Preview(); model.Update(); };
        refreshers.Add(() => thickness.Value = s.Thickness);
        Row("Thickness", thickness);
        var glowSlider = new GlassSlider(s.Glow, 140);
        glowSlider.Changed += v => { s.Glow = v; Preview(); model.Update(); };
        refreshers.Add(() => glowSlider.Value = s.Glow);
        Row("Glow", glowSlider);
        Row("Start with Windows", Toggle(() => Startup.IsOn("Lune"), v => Startup.Set("Lune", v)));

        var footer = Ui.Label($"Lune {LuneVers.Updater.Current.ToString(2)} for Windows · Made by Rizwan · updates itself", 11, null, 0.45);
        footer.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(footer);

        var card = Ui.Glass(stack, 24, 0.09, 22);
        card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Bottom;
        card.Margin = new Thickness(0, 0, 0, 92);
        card.RenderTransformOrigin = new Point(0.5, 1);
        card.RenderTransform = new ScaleTransform(0.95, 0.95);
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return card;
    }

    DateTime previewUntil;
    /// Shows the light for a moment while settings change, even when paused.
    void Preview()
    {
        model.Previewing = true;
        previewUntil = DateTime.Now.AddSeconds(2.5);
    }

    // MARK: State

    void ShowSettings(bool show)
    {
        settingsButton.Active = show;
        Ui.Show(settingsPanel, show, 220);
        var st = (ScaleTransform)settingsPanel.RenderTransform;
        Ui.To(st, ScaleTransform.ScaleXProperty, show ? 1 : 0.95, 320, Ui.Spring);
        Ui.To(st, ScaleTransform.ScaleYProperty, show ? 1 : 0.95, 320, Ui.Spring);
        if (show) Ui.Show(lyricsCard, false, 160);
        Ui.Show(main, !show && !showLyrics, 200);
        if (!show && showLyrics) Ui.Show(lyricsCard, true, 200);
    }

    public void SetLyrics(bool on)
    {
        showLyrics = on;
        if (on) lyrics.Refresh();
        Ui.Show(main, !on, 220);
        Ui.Show(lyricsCard, on, 260);
        var st = (ScaleTransform)lyricsCard.RenderTransform;
        Ui.To(st, ScaleTransform.ScaleXProperty, on ? 1 : 0.96, 360, Ui.Spring);
        Ui.To(st, ScaleTransform.ScaleYProperty, on ? 1 : 0.96, 360, Ui.Spring);
        if (settingsPanel.Visibility == Visibility.Visible) ShowSettings(false);
    }

    void SetHover(bool on)
    {
        hoverCover = on;
        // The cover grows out of its glass frame, and the window takes its color.
        double s = on ? 330.0 / 250.0 : 1;
        Ui.To(coverScale, ScaleTransform.ScaleXProperty, s, 420, Ui.Spring);
        Ui.To(coverScale, ScaleTransform.ScaleYProperty, s, 420, Ui.Spring);
        AnimateFrame(on ? 0 : 1);
        // Fade only, so the layout doesn't jump while the cover grows.
        Ui.To(lyricsButton, UIElement.OpacityProperty, on ? 0 : 1, 180);
        lyricsButton.IsHitTestVisible = !on;
    }

    void AnimateFrame(double to)
    {
        if (coverFrame.Background is SolidColorBrush b) { if (b.IsFrozen) { b = b.Clone(); coverFrame.Background = b; } Ui.To(b, Brush.OpacityProperty, to, 260); }
        if (coverFrame.BorderBrush is Brush stroke) { if (stroke.IsFrozen) { stroke = stroke.Clone(); coverFrame.BorderBrush = stroke; } Ui.To(stroke, Brush.OpacityProperty, to, 260); }
    }

    void UpdateTrack()
    {
        var m = model.Media;
        title.Text = smallTitle.Text = m.HasTrack ? m.Title : "Nothing playing";
        artist.Text = smallArtist.Text = m.HasTrack ? (m.Source.Length > 0 && m.Artist.Length > 0 ? $"{m.Artist} · {m.Source}" : m.Artist) : "Play a song in Spotify, Apple Music or a browser";
        SetArt(model.Artwork);
        RefreshControls();
    }

    void SetArt(ImageSource? art)
    {
        cover.Source = smallCover.Source = art;
        if (art is null)
        {
            var accent = model.Accent;
            var placeholder = new LinearGradientBrush(accent.Color, (accent * 0.55f).Color, 45);
            coverBox.Background = smallCoverBox.Background = placeholder;
        }
        else coverBox.Background = smallCoverBox.Background = Ui.Fill(0.1);
    }

    void UpdateState()
    {
        playGlyph.Text = smallPlayGlyph.Text = model.Media.Playing ? Ui.Glyph.Pause : Ui.Glyph.Play;
    }

    void RefreshControls()
    {
        var s = model.Settings;
        offsetLabel.Text = s.LyricsOffset == 0 ? "In sync" : $"{s.LyricsOffset:+0.00;-0.00}s";
        foreach (var r in refreshers) r();
        var accent = model.Accent;
        glow.GradientStops.Clear();
        glow.GradientStops.Add(new GradientStop(accent.WithAlpha(0.32), 0));
        glow.GradientStops.Add(new GradientStop(accent.WithAlpha(0.08), 0.55));
        glow.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
    }

    void OnFrame(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.1, now - lastTick);
        if (dt < 1 / 61.0) return;
        lastTick = now;
        if (model.Previewing && DateTime.Now > previewUntil) model.Previewing = false;
        // The cover and the color behind it breathe with the music: the real
        // level when sound is playing, a slow breath otherwise.
        var signal = model.Audio.Read();
        double target = model.Media.Playing
            ? (signal.Age < 0.5 ? Math.Min(1, signal.Energy * 0.7 + signal.Beat * 0.6) : 0.35 + 0.25 * Math.Sin(now * 2.2))
            : 0;
        pulse += (target - pulse) * (1 - Math.Exp(-dt / (target > pulse ? 0.06 : 0.25)));
        double hover = hoverCover ? 1 : 0;
        glowLayer.Opacity = 0.55 + 0.25 * pulse + 0.35 * hover;
        double beat = 1 + pulse * 0.018;
        coverFrame.RenderTransform = new ScaleTransform(beat, beat, 135, 135);
    }

    public void ToggleFullScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!fullscreen)
        {
            restoreBounds = new Rect(Left, Top, Width, Height);
            var monitors = Native.Monitors();
            var center = new Point(Left + Width / 2, Top + Height / 2);
            var source = PresentationSource.FromVisual(this);
            double scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            var px = new Point(center.X * scale, center.Y * scale);
            var m = monitors.FirstOrDefault(x => px.X >= x.Bounds.Left && px.X < x.Bounds.Right && px.Y >= x.Bounds.Top && px.Y < x.Bounds.Bottom) ?? monitors.FirstOrDefault();
            fullscreen = true;
            if (m is not null) Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, m.Bounds.Left, m.Bounds.Top, m.Bounds.Width, m.Bounds.Height, Native.SWP_SHOWWINDOW);
            Topmost = true;
        }
        else
        {
            fullscreen = false;
            Topmost = model.Settings.KeepPlayerOnTop;
            Left = restoreBounds.X; Top = restoreBounds.Y; Width = restoreBounds.Width; Height = restoreBounds.Height;
        }
        RefreshControls();
    }

    // MARK: Previews for tests and the website

    public FrameworkElement Surface => outer;
    public void Demo(string song, string by, BitmapSource? art, LyricDocument doc, double time, bool lyricsView, bool settings)
    {
        fallbackBackground.Visibility = Visibility.Visible;
        title.Text = smallTitle.Text = song;
        artist.Text = smallArtist.Text = by;
        SetArt(art);
        lyrics.SetDemo(doc, time);
        playGlyph.Text = smallPlayGlyph.Text = Ui.Glyph.Pause;
        main.Visibility = lyricsView || settings ? Visibility.Collapsed : Visibility.Visible; main.Opacity = 1;
        lyricsCard.Visibility = lyricsView ? Visibility.Visible : Visibility.Collapsed; lyricsCard.Opacity = 1; lyricsCard.RenderTransform = new ScaleTransform(1, 1);
        settingsPanel.Visibility = settings ? Visibility.Visible : Visibility.Collapsed; settingsPanel.Opacity = 1; settingsPanel.RenderTransform = new ScaleTransform(1, 1);
    }
}
