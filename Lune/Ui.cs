using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lune;

/// Building blocks for the frosted-glass player.
public static class Ui
{
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily Text = new("Segoe UI Variable Display, Segoe UI");

    public static class Glyph
    {
        public const string Play = "", Pause = "", Previous = "", Next = "", Back = "", Forward = "";
        public const string Settings = "", Refresh = "", Pin = "", Unpin = "", Light = "", Palette = "";
        public const string Wave = "", Display = "", FullScreen = "", ExitFullScreen = "", Close = "", Minimize = "";
        public const string Add = "", Remove = "", Lyrics = "", Music = "", Check = "";
    }

    public static Color White(double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), 255, 255, 255);
    public static SolidColorBrush Fill(double a) => new(White(a));

    /// The thin light edge on glass: brighter at the top.
    public static Brush Stroke(double strength = 1) => new LinearGradientBrush(White(0.30 * strength), White(0.05 * strength), 90);

    public static TextBlock Label(string text, double size, FontWeight? weight = null, double opacity = 1) => new()
    {
        Text = text, FontSize = size, FontFamily = Text, FontWeight = weight ?? FontWeights.Normal,
        Foreground = Fill(opacity), TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static TextBlock Icon(string glyph, double size, double opacity = 1) => new()
    {
        Text = glyph, FontFamily = Icons, FontSize = size, Foreground = Fill(opacity),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    /// A frosted card: faint white fill, light edge and a soft shadow.
    public static Border Glass(UIElement? child, double radius, double fill = 0.07, double padding = 0) => new()
    {
        Child = child, CornerRadius = new CornerRadius(radius), Background = Fill(fill), BorderBrush = Stroke(), BorderThickness = new Thickness(1),
        Padding = new Thickness(padding),
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.28, Color = Colors.Black, Direction = 270 },
    };

    /// Spring-like ease: a quick overshoot that settles, like SwiftUI's springs.
    public static IEasingFunction Spring => new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
    public static IEasingFunction Smooth => new CubicEase { EasingMode = EasingMode.EaseOut };

    public static void To(Animatable target, DependencyProperty property, double value, double ms, IEasingFunction? ease = null)
    {
        var a = new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Smooth };
        target.BeginAnimation(property, a, HandoffBehavior.SnapshotAndReplace);
    }

    public static void To(UIElement target, DependencyProperty property, double value, double ms, IEasingFunction? ease = null)
    {
        var a = new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Smooth };
        target.BeginAnimation(property, a, HandoffBehavior.SnapshotAndReplace);
    }

    /// Fades an element in or out, then collapses it once hidden.
    public static void Show(UIElement e, bool show, double ms = 220)
    {
        if (show) e.Visibility = Visibility.Visible;
        var a = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Smooth };
        if (!show) a.Completed += (_, _) => { if (e.Opacity < 0.01) e.Visibility = Visibility.Collapsed; };
        e.BeginAnimation(UIElement.OpacityProperty, a, HandoffBehavior.SnapshotAndReplace);
    }
}

/// Round or capsule glass button: lifts on hover, squashes on press.
public sealed class Pill : Border
{
    readonly ScaleTransform scale = new(1, 1);
    bool hover, down, active;
    readonly double baseFill;
    Color? tint;
    public event Action? Click;

    public Pill(UIElement content, double width, double height, bool round = true, double fill = 0.08)
    {
        Child = content;
        baseFill = fill;
        if (width > 0) Width = width;
        Height = height;
        MinWidth = height;
        CornerRadius = new CornerRadius(height / 2);
        BorderThickness = new Thickness(1);
        BorderBrush = Ui.Stroke(0.9);
        Padding = round ? new Thickness(0) : new Thickness(height * 0.42, 0, height * 0.42, 0);
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = scale;
        Cursor = Cursors.Hand;
        Background = new SolidColorBrush(Ui.White(fill));
        MouseEnter += (_, _) => { hover = true; Refresh(); };
        MouseLeave += (_, _) => { hover = false; down = false; Refresh(); };
        MouseLeftButtonDown += (_, e) => { down = true; Refresh(); e.Handled = true; CaptureMouse(); };
        MouseLeftButtonUp += (_, e) =>
        {
            bool fire = down && IsMouseOver;
            down = false; ReleaseMouseCapture(); Refresh(); e.Handled = true;
            if (fire) Click?.Invoke();
        };
    }

    public static Pill Round(string glyph, double size, double iconSize = 0, double fill = 0.08) =>
        new(Ui.Icon(glyph, iconSize > 0 ? iconSize : size * 0.4), size, size, true, fill);

    public static Pill Text(string text, double height, string? glyph = null, double fontSize = 0)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        double fs = fontSize > 0 ? fontSize : height * 0.36;
        if (glyph is not null) row.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.Icons, FontSize = fs, Foreground = Brushes.White, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, FontFamily = Ui.Text, FontSize = fs, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        return new Pill(row, 0, height, false);
    }

    /// Lit with a color (the active choice in a group).
    public Color? Tint { get => tint; set { tint = value; Refresh(); } }
    public bool Active { get => active; set { active = value; Refresh(); } }

    void Refresh()
    {
        double s = down ? 0.9 : hover ? 1.05 : 1;
        Ui.To(scale, ScaleTransform.ScaleXProperty, s, down ? 90 : 260, down ? Ui.Smooth : Ui.Spring);
        Ui.To(scale, ScaleTransform.ScaleYProperty, s, down ? 90 : 260, down ? Ui.Smooth : Ui.Spring);
        var target = tint is { } t ? Color.FromArgb(90, t.R, t.G, t.B) : Ui.White(down ? baseFill + 0.08 : active ? baseFill + 0.1 : hover ? baseFill + 0.05 : baseFill);
        var brush = Background as SolidColorBrush;
        if (brush is null || brush.IsFrozen) { brush = new SolidColorBrush(brush?.Color ?? target); Background = brush; }
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target, TimeSpan.FromMilliseconds(180)));
    }
}

/// A glass slider: drag or click anywhere on the track.
public sealed class GlassSlider : Grid
{
    readonly Border fill = new() { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Background = Ui.Fill(0.85) };
    readonly Border knob = new() { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.35 } };
    double value;
    public event Action<double>? Changed;

    public GlassSlider(double value, double width)
    {
        Width = width; Height = 22;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Fill(0.16), VerticalAlignment = VerticalAlignment.Center });
        fill.Height = 6; fill.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(fill);
        Children.Add(knob);
        Value = value;
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); Set(e.GetPosition(this).X); e.Handled = true; };
        MouseMove += (_, e) => { if (IsMouseCaptured) Set(e.GetPosition(this).X); };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
        SizeChanged += (_, _) => Layout();
    }

    void Set(double x)
    {
        Value = Math.Clamp((x - 9) / Math.Max(1, ActualWidth - 18), 0, 1);
        Changed?.Invoke(value);
    }

    public double Value { get => value; set { this.value = Math.Clamp(value, 0, 1); Layout(); } }

    void Layout()
    {
        double w = double.IsNaN(Width) ? ActualWidth : Width;
        double x = value * Math.Max(0, w - 18);
        fill.Width = x + 9;
        knob.Margin = new Thickness(x, 0, 0, 0);
    }
}

/// An on/off switch.
public sealed class GlassToggle : Border
{
    readonly Border knob = new() { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TranslateTransform shift = new();
    bool on;
    Color accent = Color.FromRgb(255, 60, 110);
    public event Action<bool>? Changed;

    public GlassToggle(bool on)
    {
        Width = 42; Height = 24; CornerRadius = new CornerRadius(12); BorderThickness = new Thickness(1); BorderBrush = Ui.Stroke();
        Padding = new Thickness(2);
        Cursor = Cursors.Hand;
        knob.RenderTransform = shift;
        Child = knob;
        Background = new SolidColorBrush(Ui.White(0.12));
        On = on;
        MouseLeftButtonUp += (_, e) => { On = !On; Changed?.Invoke(On); e.Handled = true; };
    }

    public Color Accent { get => accent; set { accent = value; On = on; } }

    public bool On
    {
        get => on;
        set
        {
            on = value;
            Ui.To(shift, TranslateTransform.XProperty, on ? 18 : 0, 260, Ui.Spring);
            var brush = (SolidColorBrush)Background;
            if (brush.IsFrozen) { brush = brush.Clone(); Background = brush; }
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Color.FromArgb(200, accent.R, accent.G, accent.B) : Ui.White(0.12), TimeSpan.FromMilliseconds(200)));
        }
    }
}
