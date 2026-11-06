using System.Globalization;
using System.Text.RegularExpressions;

namespace LuneVers;

public sealed record LyricWord(string Text, double Start, double End, int Line);

public sealed class LyricDocument
{
    public List<LyricWord> Words { get; init; } = new();
    /// True when every word came with its own timestamp.
    public bool Exact { get; init; }
    public int LineCount { get; init; }

    /// Words grouped by line, in order.
    public List<List<LyricWord>> Lines()
    {
        var lines = new List<List<LyricWord>>();
        foreach (var w in Words)
        {
            if (lines.Count == 0 || lines[^1][0].Line != w.Line) lines.Add(new List<LyricWord> { w });
            else lines[^1].Add(w);
        }
        return lines;
    }
}

/// Synced lyrics (LRC) with word timing. Lines without per-word stamps share
/// the line's time between words by syllable weight, like the Mac apps.
public static class Lrc
{
    static readonly Regex LineRx = new(@"\[(\d+):(\d+(?:\.\d+)?)\]", RegexOptions.Compiled);
    static readonly Regex WordRx = new(@"<(\d+):(\d+(?:\.\d+)?)>", RegexOptions.Compiled);
    static readonly Regex OffsetRx = new(@"\[offset:([+-]?\d+)\]", RegexOptions.Compiled);

    static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    static double Stamp(Match m) => Num(m.Groups[1].Value) * 60 + Num(m.Groups[2].Value);

    /// The song's plain title: drops "(feat. …)", "[…]" and " - Remix" style suffixes.
    public static string PlainTitle(string title)
    {
        var t = Regex.Replace(title, @"\s*[\(\[][^\)\]]*[\)\]]", "");
        var dash = t.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0) t = t[..dash];
        t = Regex.Replace(t, @"\s+(feat\.?|ft\.?|featuring)\s.*$", "", RegexOptions.IgnoreCase).Trim();
        return t.Length == 0 ? title : t;
    }

    /// The first credited artist ("A, B & C" → "A").
    public static string MainArtist(string artist)
    {
        var first = artist.Split(',', '&')[0];
        var a = Regex.Replace(first, @"\s+(feat\.?|ft\.?|x)\s.*$", "", RegexOptions.IgnoreCase).Trim();
        return a.Length == 0 ? artist : a;
    }

    public static LyricDocument Parse(string input, double duration = 0)
    {
        double offset = 0;
        var om = OffsetRx.Match(input);
        if (om.Success) offset = Num(om.Groups[1].Value) / 1000;

        var lines = new List<(double Time, string Text)>();
        foreach (var raw in input.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var matches = LineRx.Matches(line);
            if (matches.Count == 0) continue;
            var last = matches[^1];
            var text = line[(last.Index + last.Length)..].Trim();
            foreach (Match m in matches) lines.Add((Math.Max(0, Stamp(m) + offset), text));
        }
        lines = lines.OrderBy(l => l.Time).ToList();

        var words = new List<LyricWord>();
        bool exact = true;
        int lineNumber = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var (start0, text) = lines[i];
            double nextStart = i + 1 < lines.Count ? lines[i + 1].Time : Math.Max(start0 + 4, duration);
            var markers = WordRx.Matches(text);
            bool added = false;
            if (markers.Count > 0)
            {
                for (int j = 0; j < markers.Count; j++)
                {
                    var marker = markers[j];
                    double start = Stamp(marker) + offset;
                    int textStart = marker.Index + marker.Length;
                    int textEnd = j + 1 < markers.Count ? markers[j + 1].Index : text.Length;
                    var piece = text[textStart..textEnd].Trim();
                    double end = j + 1 < markers.Count ? Stamp(markers[j + 1]) + offset : Math.Min(nextStart, start + 1.2);
                    if (piece.Length > 0)
                    {
                        words.Add(new LyricWord(piece, Math.Max(0, start), Math.Max(start + 0.03, end), lineNumber));
                        added = true;
                    }
                }
            }
            else
            {
                var pieces = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (pieces.Length > 0) exact = false;
                // Sung lines rarely fill the whole gap to the next line: leave a
                // short breath at the end, then share time by syllable weight.
                double span = Math.Max(0.2, nextStart - start0);
                double sung = pieces.Length > 1 ? Math.Min(span, Math.Max(span * 0.82, span - 1.2)) : Math.Min(span, 1.4);
                var weights = pieces.Select(SyllableWeight).ToArray();
                double total = weights.Sum();
                double cursor = start0;
                for (int k = 0; k < pieces.Length; k++)
                {
                    double next = cursor + sung * weights[k] / Math.Max(0.001, total);
                    words.Add(new LyricWord(pieces[k], cursor, next, lineNumber));
                    cursor = next;
                    added = true;
                }
            }
            if (added) lineNumber++;
        }
        var sorted = words.OrderBy(w => w.Start).ToList();
        return new LyricDocument { Words = sorted, Exact = exact && sorted.Count > 0, LineCount = lineNumber };
    }

    /// Rough sung length of a word: vowel groups, plus a pause after punctuation.
    public static double SyllableWeight(string word)
    {
        var lower = word.ToLowerInvariant();
        int groups = 0;
        bool inVowel = false;
        foreach (var ch in lower)
        {
            bool vowel = "aeiouy".IndexOf(ch) >= 0;
            if (vowel && !inVowel) groups++;
            inVowel = vowel;
        }
        if (lower.EndsWith('e') && groups > 1 && !lower.EndsWith("le")) groups--;
        double weight = 0.55 + Math.Max(1, groups) * 0.45;
        if (word.Length > 0 && ",.!?;:".IndexOf(word[^1]) >= 0) weight += 0.35;
        return weight;
    }

    /// Last word that has started by `time`, or -1.
    public static int LastStarted(IReadOnlyList<LyricWord> words, double time)
    {
        if (words.Count == 0 || time < words[0].Start) return -1;
        int low = 0, high = words.Count;
        while (low < high) { int mid = (low + high) / 2; if (words[mid].Start <= time) low = mid + 1; else high = mid; }
        return low - 1;
    }

    /// The word being sung at `time`, or -1 between words.
    public static int ActiveIndex(IReadOnlyList<LyricWord> words, double time)
    {
        int i = LastStarted(words, time);
        return i >= 0 && time < words[i].End ? i : -1;
    }
}
