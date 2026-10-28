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

    public static (bool Ok, string Text) ParseReply(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int status = 200;
            if (root.TryGetProperty("responseStatus", out var st))
                status = st.ValueKind == JsonValueKind.Number ? st.GetInt32() : int.TryParse(st.GetString(), out var n) ? n : 0;
            if (status != 200)
                return (false, status == 429 ? "Jatah terjemahan gratis hari ini habis. Tekan Ctrl+Enter untuk membuka Google Translate." : "Terjemahan gagal (kode " + status + ").");
            if (root.TryGetProperty("responseData", out var data) && data.TryGetProperty("translatedText", out var t) && t.GetString() is { Length: > 0 } text)
                return (true, WebUtility.HtmlDecode(text).Trim());
            return (false, "Tidak ada hasil terjemahan.");
        }
        catch (JsonException) { return (false, "Balasan terjemahan tidak terbaca."); }
    }
}
