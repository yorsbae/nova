using System.Net.Http;
using System.Text.Json;
using Nova.Core.Logging;
using Nova.Core.Media;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Opt-in synced lyrics from lrclib.net. Does nothing unless the user switched lyrics on.
/// Only the artist, the track title and (if known) the track length leave the PC.
/// </summary>
public sealed class LyricsService
{
    static readonly HttpClient Http = CreateClient();
    readonly Dictionary<string, IReadOnlyList<LyricLine>?> _cache = new();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 512 * 1024 };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("NOVA/2.2");
        return c;
    }

    /// <summary>Synced lines for the track, or null if none were found / the service is unreachable. Never throws.</summary>
    public async Task<IReadOnlyList<LyricLine>?> FetchAsync(string artist, string title, TimeSpan? duration, CancellationToken ct)
    {
        title = (title ?? "").Trim();
        artist = (artist ?? "").Trim();
        if (title.Length == 0) return null;

        var key = (artist + "\u0001" + title).ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached)) return cached;

        // Browser tabs give titles like "Artist - Song (Lyric Video)": try the plain song name first, then the title as it came.
        var cleanTitle = TrackName.CleanTitle(title, artist);
        var cleanArtist = TrackName.CleanArtist(artist);
        var candidates = new List<(string Title, string Artist)> { (cleanTitle, cleanArtist) };
        if (!string.Equals(cleanTitle, title, StringComparison.Ordinal) || !string.Equals(cleanArtist, artist, StringComparison.Ordinal))
            candidates.Add((title, artist));

        try
        {
            IReadOnlyList<LyricLine>? lines = null;
            foreach (var (t, a) in candidates)
            {
                var url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(t);
                if (a.Length > 0) url += "&artist_name=" + Uri.EscapeDataString(a);
                if (duration is { } d && d > TimeSpan.Zero) url += "&duration=" + (int)Math.Round(d.TotalSeconds);

                using var response = await Http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("syncedLyrics", out var synced) &&
                        synced.ValueKind == JsonValueKind.String)
                    {
                        var parsed = Lrc.Parse(synced.GetString());
                        if (parsed.Count > 0) { lines = parsed; break; }
                    }
                }
                else if ((int)response.StatusCode != 404)
                {
                    return null;   // temporary problem: don't cache, try again next time
                }
            }

            if (_cache.Count >= 30) _cache.Clear();
            _cache[key] = lines;
            return lines;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) { FileLog.Write($"Lyrics lookup failed: {ex.GetType().Name}"); return null; }
    }
}
