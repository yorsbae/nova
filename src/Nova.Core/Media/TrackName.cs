using System.Text.RegularExpressions;

namespace Nova.Core.Media;

/// <summary>
/// Browser tabs and video sites give messy song titles ("Artist - Song (Lyric Video)"). Lyrics services want the plain song name,
/// so this removes the artist prefix and the usual "(Official Video)" style noise. Never returns an empty string for a non-empty title.
/// </summary>
public static class TrackName
{
    static readonly Regex Noise = new(
        @"\s*[\(\[\{][^\)\]\}]*\b(lyrics?|lirik|official|video|audio|mv|m/v|visuali[sz]er|hd|hq|4k|clip|music video|video musik|full album|remaster(ed)?)\b[^\)\]\}]*[\)\]\}]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Topic = new(@"\s*-\s*Topic\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);

    public static string CleanArtist(string? artist)
    {
        var a = Topic.Replace((artist ?? "").Trim(), "").Trim();
        return a;
    }

    public static string CleanTitle(string? title, string? artist)
    {
        var original = (title ?? "").Trim();
        if (original.Length == 0) return "";
        var t = Spaces.Replace(Noise.Replace(original, ""), " ").Trim();

        var a = CleanArtist(artist);
        if (a.Length > 0)
        {
            foreach (var sep in new[] { " - ", " – ", " — ", " | " })
            {
                if (t.StartsWith(a + sep, StringComparison.OrdinalIgnoreCase)) { t = t[(a.Length + sep.Length)..].Trim(); break; }
                if (t.EndsWith(sep + a, StringComparison.OrdinalIgnoreCase)) { t = t[..^(a.Length + sep.Length)].Trim(); break; }
            }
        }
        return t.Length == 0 ? original : t;
    }
}
