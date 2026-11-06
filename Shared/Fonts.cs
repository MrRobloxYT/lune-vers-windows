using System.Windows.Media;

namespace LuneVers;

/// Inter, a free font close to Apple's San Francisco, ships inside the apps
/// so text looks the same as on the Mac on every PC.
public static class Fonts
{
    static readonly Uri Base = new("pack://application:,,,/LuneVers.Shared;component/");
    public static readonly FontFamily Inter = new(Base, "./Fonts/#Inter, Segoe UI Variable Display, Segoe UI");

    /// Free stand-ins bundled for the Mac app's fonts (Apple's own can't be
    /// shipped on Windows), with the Mac font each one stands in for.
    public static readonly (string Name, string Mac)[] Bundled =
    {
        ("Inter", "SF Pro"), ("Nunito", "SF Rounded"), ("Source Serif 4", "New York"), ("Arimo", "Helvetica Neue"),
        ("Nunito Sans", "Avenir Next"), ("Jost", "Futura"), ("Playfair Display", "Didot"),
    };

    public static FontFamily Family(string name)
    {
        if (name == "Inter") return Inter;
        if (Bundled.Any(b => b.Name == name)) return new FontFamily(Base, $"./Fonts/#{name}, Segoe UI");
        return new FontFamily(name + ", Segoe UI");
    }

    public static string Label(string name) => Bundled.FirstOrDefault(b => b.Name == name) is { Mac: { } mac } && mac.Length > 0 ? $"{name} (like {mac})" : name.Replace(" Variable Display", "");
}
