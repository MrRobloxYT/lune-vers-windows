using System.Diagnostics;
using System.IO;

namespace LuneVers;

/// The nine styles, in the Mac app's menu order. Timing is the same in all
/// of them; only the way the words arrive changes.
public enum LyricStyle { Ship, Fisheye, Visual, FisheyeVisual, Codestyle, CodestyleVisual, Branches, Web, Constellation }

public sealed class VersSettings
{
    public bool AutoStyle { get; set; } = true;
    public LyricStyle Style { get; set; } = LyricStyle.Ship;
    public string Font { get; set; } = "Inter";
    public double Size { get; set; } = 1;
    public string Display { get; set; } = "main";
    public bool AutoColor { get; set; } = true;
    public string Lyric { get; set; } = "#F4F4F8";
    public string Highlight { get; set; } = "#FF4D8A";
    public string Secondary { get; set; } = "#9AA0B4";
    public bool OnlyWhilePlaying { get; set; } = true;
    // Like the Mac: a black screen while a song with lyrics plays, floating above windows.
    public bool BlackBackground { get; set; } = true;
    public bool AboveWindows { get; set; } = true;
    public int Version { get; set; }
    public string LastVersion { get; set; } = "";
    public double Offset { get; set; }    // seconds; positive shows lyrics earlier

    public static readonly string[] Fonts = { "Inter", "Segoe UI Variable Display", "Georgia", "Bahnschrift", "Cascadia Code" };
}

public static class StyleInfo
{
    public static string Name(LyricStyle s) => s switch
    {
        LyricStyle.FisheyeVisual => "Fisheye Visual", LyricStyle.CodestyleVisual => "Codestyle Visual", _ => s.ToString(),
    };
    public static bool Icons(LyricStyle s) => s is LyricStyle.Visual or LyricStyle.FisheyeVisual or LyricStyle.CodestyleVisual;
    public static string Summary(LyricStyle s) => s switch
    {
        LyricStyle.Ship => "The last few words stack into a wall that drifts in 3D",
        LyricStyle.Fisheye => "Previous, current and next word under a glass lens",
        LyricStyle.Visual => "The drifting stack with icons popping in beside words",
        LyricStyle.FisheyeVisual => "The lens, with icons switched on",
        LyricStyle.Codestyle => "Each line typed out as code with syntax colors",
        LyricStyle.CodestyleVisual => "Codestyle with an icon before some rows",
        LyricStyle.Branches => "A wavy stem grows; every word sprouts a branch",
        LyricStyle.Web => "A spider web; each word spins a strand from the hub",
        _ => "Words become stars joined into a constellation",
    };

    /// Auto style: fast, dense lyrics (rap) get the punchy styles, slow songs
    /// the airy ones. The same song always gets the same style.
    public static LyricStyle? Suggest(IReadOnlyList<LyricWord> words, string title, string artist)
    {
        if (words.Count < 4) return null;
        var spans = new Dictionary<int, (double Start, double End)>();
        foreach (var w in words)
            spans[w.Line] = spans.TryGetValue(w.Line, out var s) ? (Math.Min(s.Start, w.Start), Math.Max(s.End, w.End)) : (w.Start, w.End);
        double singing = spans.Values.Sum(s => Math.Max(0.3, s.End - s.Start));
        double rate = words.Count / Math.Max(1, singing);
        double longWords = words.Count(w => w.Text.Length >= 7) / (double)words.Count;
        // Same groups and the same hash as the Mac, so a song gets the same style on both.
        LyricStyle[] group = rate > 3.0 ? new[] { LyricStyle.Ship, LyricStyle.Codestyle, LyricStyle.CodestyleVisual }
            : rate > 2.1 ? (longWords > 0.12 ? new[] { LyricStyle.Visual, LyricStyle.Branches, LyricStyle.CodestyleVisual } : new[] { LyricStyle.Visual, LyricStyle.Branches, LyricStyle.FisheyeVisual })
            : new[] { LyricStyle.Constellation, LyricStyle.Web, LyricStyle.Fisheye, LyricStyle.Constellation };
        ulong h = 1469598103934665603;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes((title + "|" + artist).ToLowerInvariant())) { h ^= b; h *= 1099511628211; }
        return group[(int)(h % (ulong)group.Length)];
    }
}

/// How Lune's player reads and changes Vers's settings: Vers keeps them in
/// %APPDATA%\Vers\settings.json and reloads when the file changes.
public static class VersLink
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vers");
    public static string SettingsFile => Path.Combine(Folder, "settings.json");
    static string StateFile => Path.Combine(Folder, "state.txt");

    public static VersSettings Load() => Store.Load<VersSettings>("Vers");
    public static void Save(VersSettings s) => Store.Save("Vers", s);

    public static bool Running()
    {
        try { return Process.GetProcessesByName("Vers").Length > 0; } catch { return false; }
    }

    /// The style Vers's Auto picked for the current song, if any.
    public static string? AutoPick()
    {
        try { var t = File.Exists(StateFile) ? File.ReadAllText(StateFile).Trim() : ""; return t.Length > 0 ? t : null; } catch { return null; }
    }

    public static void WriteAutoPick(LyricStyle? pick)
    {
        try { Directory.CreateDirectory(Folder); File.WriteAllText(StateFile, pick is { } p ? StyleInfo.Name(p) : ""); } catch { }
    }
}
