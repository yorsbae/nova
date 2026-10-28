using System.Net.Http;
using System.Text;
using Nova.Core.Ask;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

/// <summary>Sends the conversation to Google's Gemini API. Only used when the person picked the Gemini engine and entered their own API key.</summary>
public static class GeminiClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<(bool Ok, string Text)> AskAsync(string apiKey, string model, IReadOnlyList<AskTurn> history, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return (false, "Isi Gemini API key di Pengaturan dulu.");
        if (!AskSearch.IsValidModel(model)) model = AskSearch.DefaultModel;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
            {
                Content = new StringContent(AskSearch.BuildGeminiRequest(history), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-goog-api-key", apiKey.Trim());
            using var res = await Http.SendAsync(req, ct).ConfigureAwait(true);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(true);
            return AskSearch.ParseGeminiReply(body);
        }
        catch (OperationCanceledException) { return (false, "Dibatalkan atau terlalu lama."); }
        catch (HttpRequestException ex)
        {
            FileLog.Write($"Gemini request failed: {ex.GetType().Name}");   // no question text is ever logged
            return (false, "Tidak bisa terhubung ke Gemini. Cek internet.");
        }
    }
}
