using System.Globalization;
using System.Text.RegularExpressions;

namespace Nova.Core.Media;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Parses time-synced lyrics in the common "[mm:ss.xx] text" format and finds the line for a playback position.</summary>
public static class Lrc
{
    const int MaxLines = 2000;
    static readonly Regex Stamp = new(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

    /// <summary>Lines sorted by time. Tags such as [ar:...] and unsynced text are ignored. Never throws.</summary>
    public static IReadOnlyList<LyricLine> Parse(string? text)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t');
            var matches = Stamp.Matches(line);
            if (matches.Count == 0) continue;

            var lyric = Stamp.Replace(line, "").Trim();
            foreach (Match m in matches)
            {
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var min) ||
                    !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var sec)) continue;
                double frac = 0;
                if (m.Groups[3].Success)
                {
                    var digits = m.Groups[3].Value;
                    if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var f))
                        frac = f / Math.Pow(10, digits.Length);
                }
                result.Add(new LyricLine(TimeSpan.FromSeconds(min * 60 + sec + frac), lyric));
                if (result.Count >= MaxLines) break;
            }
            if (result.Count >= MaxLines) break;
        }
        return result.OrderBy(l => l.Time).ToList();
    }

    /// <summary>Index of the line that is current at <paramref name="position"/>, or -1 before the first line.</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].Time <= position) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }
}
