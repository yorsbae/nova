using System.Net;
using System.Text;
using System.Text.Json;

namespace Nova.Core.Translate;

/// <summary>Pure helpers for the "Terjemah" page: language list, request address, reply parsing. The service is MyMemory (free, no key).</summary>
public static class Translation
{
    /// <summary>MyMemory accepts at most 500 bytes of text per request.</summary>
    public const int MaxBytes = 500;

    public static readonly (string Code, string Chip, string Name)[] Languages =
    {
        ("id", "ID", "Indonesia"), ("en", "EN", "English"), ("ja", "JA", "日本語"),
        ("ar", "AR", "العربية"), ("zh-CN", "ZH", "中文"), ("ko", "KO", "한국어")
    };

    public static bool IsKnownLanguage(string? code) => Languages.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    public static bool FitsLimit(string text) => Encoding.UTF8.GetByteCount(text ?? "") <= MaxBytes;

    /// <summary>Source language is detected automatically.</summary>
    public static string BuildUrl(string text, string targetCode) =>
        "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString((text ?? "").Trim())
        + "&langpair=" + Uri.EscapeDataString("Autodetect|" + targetCode);

    /// <summary>The same text in Google Translate in the browser (for long texts or when the free service is out of quota).</summary>
    public static string BuildBrowserUrl(string text, string targetCode) =>
        "https://translate.google.com/?sl=auto&tl=" + Uri.EscapeDataString(targetCode) + "&op=translate&text=" + Uri.EscapeDataString((text ?? "").Trim());

    /// <summary>First translation only (kept for simple callers).</summary>
    public static (bool Ok, string Text) ParseReply(string json)
    {
        var (ok, versions, error) = ParseVersions(json, 1, "");
        return ok ? (true, versions[0]) : (false, error);
    }

    /// <summary>
    /// All distinct translations MyMemory offers, best first (v1 = its main answer, then the other matches), at most <paramref name="max"/>.
    /// A "version" that is just the source text unchanged is skipped unless nothing else exists.
    /// </summary>
    public static (bool Ok, List<string> Versions, string Error) ParseVersions(string json, int max, string source)
    {
        var none = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int status = 200;
            if (root.TryGetProperty("responseStatus", out var st))
                status = st.ValueKind == JsonValueKind.Number ? st.GetInt32() : int.TryParse(st.GetString(), out var n) ? n : 0;
            if (status != 200)
                return (false, none, status == 429 ? "Jatah terjemahan gratis hari ini habis. Tekan Ctrl+Enter untuk membuka Google Translate." : "Terjemahan gagal (kode " + status + ").");

            var all = new List<string>();
            void Add(string? raw)
            {
                var t = WebUtility.HtmlDecode(raw ?? "").Trim();
                if (t.Length > 0 && !all.Contains(t, StringComparer.OrdinalIgnoreCase)) all.Add(t);
            }
            if (root.TryGetProperty("responseData", out var data) && data.TryGetProperty("translatedText", out var main)) Add(main.GetString());
            if (root.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Array)
                foreach (var m in matches.EnumerateArray())
                    if (m.TryGetProperty("translation", out var tr) && tr.ValueKind == JsonValueKind.String) Add(tr.GetString());

            var src = (source ?? "").Trim();
            var versions = all.Where(v => !string.Equals(v, src, StringComparison.OrdinalIgnoreCase)).Take(Math.Max(1, max)).ToList();
            if (versions.Count == 0) versions = all.Take(1).ToList();
            return versions.Count > 0 ? (true, versions, "") : (false, none, "Tidak ada hasil terjemahan.");
        }
        catch (JsonException) { return (false, none, "Balasan terjemahan tidak terbaca."); }
    }
}
