using System.Windows.Media;

namespace LuneVers;

/// Inter, a free font close to Apple's San Francisco, ships inside the apps
/// so text looks the same as on the Mac on every PC.
public static class Fonts
{
    static readonly Uri Base = new("pack://application:,,,/LuneVers.Shared;component/");
    public static readonly FontFamily Inter = new(Base, "./Fonts/#Inter, Segoe UI Variable Display, Segoe UI");

    public static FontFamily Family(string name) => name == "Inter" ? Inter : new FontFamily(name + ", Segoe UI");
}
