using System.Net.Http;
using Nova.Core.Logging;
using Nova.Core.Translate;

namespace Nova.App.Integrations.Windows;

/// <summary>Sends the text you typed to MyMemory (api.mymemory.translated.net) when you press Enter on the Terjemah page. Nothing else is sent; the text is never logged.</summary>
public static class TranslateClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Returns the translations (v1, v2, …) or an error text.</summary>
    public static async Task<(bool Ok, List<string> Versions, string Error)> TranslateAsync(string text, string target, int versions, CancellationToken ct)
    {
        if (!Translation.FitsLimit(text)) return (false, new List<string>(), "Teks terlalu panjang untuk terjemahan gratis (maks ±500 byte). Tekan Ctrl+Enter untuk Google Translate.");
        try
        {
            var json = await Http.GetStringAsync(Translation.BuildUrl(text, target), ct).ConfigureAwait(true);
            return Translation.ParseVersions(json, versions, text);
        }
        catch (OperationCanceledException) { return (false, new List<string>(), "Dibatalkan atau terlalu lama."); }
        catch (HttpRequestException ex)
        {
            FileLog.Write($"Translate request failed: {ex.GetType().Name}");
            return (false, new List<string>(), "Tidak bisa terhubung ke layanan terjemahan. Cek internet.");
        }
    }
}
