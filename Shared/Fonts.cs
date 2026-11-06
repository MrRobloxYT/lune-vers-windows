using System.Windows.Media;

namespace LuneVers;

/// Inter, a free font close to Apple's San Francisco, ships inside the apps
/// so text looks the same as on the Mac on every PC.
public static class Fonts
{
    static readonly Uri Base = new("pack://application:,,,/LuneVers.Shared;component/");
    public static readonly FontFamily Inter = new(Base, "./Fonts/#Inter, Segoe UI Variable Display, Segoe UI");

    /// The Mac app's fonts, in its menu order. Apple's fonts can't be shipped
    /// on Windows, so each comes with a free stand-in; if the Apple font is
    /// installed on the PC, that's used instead.
    public static readonly (string Name, string Mac, string[] Installed)[] Bundled =
    {
        ("Inter", "SF Pro", new[] { "SF Pro Display", "SF Pro Text", "SF Pro" }),
        ("Nunito", "SF Rounded", new[] { "SF Pro Rounded", "SF Rounded" }),
        ("Source Serif 4", "New York", new[] { "New York", "New York Medium" }),
        ("Arimo", "Helvetica Neue", new[] { "Helvetica Neue", "Helvetica" }),
        ("Nunito Sans", "Avenir Next", new[] { "Avenir Next", "Avenir" }),
        ("Jost", "Futura", new[] { "Futura", "Futura PT" }),
        ("Playfair Display", "Didot", new[] { "Didot" }),
    };

    static readonly HashSet<string> System = LoadSystem();
    static HashSet<string> LoadSystem()
    {
        try { return global::System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase); }
        catch { return new HashSet<string>(); }
    }

    /// The Apple font itself, when it's installed on this PC.
    public static string? InstalledApple(string name) =>
        Bundled.FirstOrDefault(b => b.Name == name) is { Installed: { } names } ? names.FirstOrDefault(System.Contains) : null;

    public static FontFamily Family(string name)
    {
        if (InstalledApple(name) is { } apple) return new FontFamily(apple + ", Segoe UI");
        if (name == "Inter") return Inter;
        if (Bundled.Any(b => b.Name == name)) return new FontFamily(Base, $"./Fonts/#{name}, Segoe UI");
        return new FontFamily(name + ", Segoe UI");
    }

    /// The name shown in menus: the Mac font's name, plus "style" when it's the stand-in.
    public static string Label(string name)
    {
        var b = Bundled.FirstOrDefault(x => x.Name == name);
        if (b.Mac is null) return name.Replace(" Variable Display", "");
        return InstalledApple(name) is not null ? b.Mac : $"{b.Mac} style";
    }

    public static string Detail(string name)
    {
        var b = Bundled.FirstOrDefault(x => x.Name == name);
        if (b.Mac is null) return name;
        return InstalledApple(name) is { } apple ? $"{apple} (installed on this PC)" : $"{name}, a free font close to {b.Mac}";
    }
}
