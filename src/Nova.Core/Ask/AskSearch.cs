using System.Text;
using System.Text.Json;

namespace Nova.Core.Ask;

/// <summary>Where a question goes: answered inside the notch by Gemini, or opened in the browser.</summary>
public enum AskEngine { Gemini, GoogleAi, Google, Brave }

/// <summary>One line of the conversation shown in the notch.</summary>
public sealed record AskTurn(bool FromUser, string Text);

/// <summary>Pure helpers for the "Tanya" page: picking the engine from a prefix, building search URLs, and the Gemini request / reply JSON.</summary>
public static class AskSearch
{
    public const string DefaultModel = "gemini-flash-latest";
    const int MaxHistory = 12;

    public static string Label(AskEngine e) => e switch
    {
        AskEngine.Gemini => "Gemini",
        AskEngine.GoogleAi => "AI Mode",
        AskEngine.Google => "Google",
        _ => "Brave"
    };

    public static string SettingName(AskEngine e) => e switch
    {
        AskEngine.Gemini => "gemini",
        AskEngine.GoogleAi => "googleai",
        AskEngine.Google => "google",
        _ => "brave"
    };

    public static bool TryParseEngine(string? text, out AskEngine engine)
    {
        foreach (var e in Enum.GetValues<AskEngine>())
            if (string.Equals(SettingName(e), (text ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) { engine = e; return true; }
        engine = AskEngine.GoogleAi; return false;
    }

    /// <summary>"b cuaca" → Brave, "g cuaca" → Google, "ai cuaca" → AI Mode, "? cuaca" → Gemini in the notch. Otherwise the fallback engine.</summary>
    public static (AskEngine Engine, string Query) ParsePrefix(string text, AskEngine fallback)
    {
        var t = (text ?? "").Trim();
        (string prefix, AskEngine engine)[] table =
        {
            ("b", AskEngine.Brave), ("g", AskEngine.Google), ("ai", AskEngine.GoogleAi), ("?", AskEngine.Gemini)
        };
        foreach (var (prefix, engine) in table)
            if (t.Length > prefix.Length + 1 && t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && t[prefix.Length] == ' ')
            {
                var rest = t[(prefix.Length + 1)..].Trim();
                if (rest.Length > 0) return (engine, rest);
            }
        return (fallback, t);
    }

    /// <summary>The https address for a browser engine. Gemini has no address (it is answered in the notch), so it falls back to AI Mode.</summary>
    public static string BuildUrl(AskEngine engine, string query)
    {
        var q = Uri.EscapeDataString((query ?? "").Trim());
        return engine switch
        {
            AskEngine.Brave => "https://search.brave.com/search?q=" + q,
            AskEngine.Google => "https://www.google.com/search?q=" + q,
            _ => "https://www.google.com/search?udm=50&q=" + q   // udm=50 opens Google's AI Mode
        };
    }

    public static bool IsValidModel(string? model) =>
        !string.IsNullOrEmpty(model) && model.Length <= 64 && model.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    /// <summary>JSON body for Gemini generateContent: the last turns of the conversation plus a short system instruction.</summary>
    public static string BuildGeminiRequest(IReadOnlyList<AskTurn> history)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartObject("systemInstruction");
            w.WriteStartArray("parts");
            w.WriteStartObject();
            w.WriteString("text", "Jawab singkat, jelas dan langsung ke inti (maksimal sekitar 120 kata) dalam bahasa yang dipakai penanya. Tanpa markdown berat.");
            w.WriteEndObject(); w.WriteEndArray(); w.WriteEndObject();
            w.WriteStartArray("contents");
            foreach (var turn in history.Skip(Math.Max(0, history.Count - MaxHistory)))
            {
                w.WriteStartObject();
                w.WriteString("role", turn.FromUser ? "user" : "model");
                w.WriteStartArray("parts");
                w.WriteStartObject(); w.WriteString("text", turn.Text); w.WriteEndObject();
                w.WriteEndArray(); w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Reads a Gemini reply. On success Text is the answer; otherwise Text is a short reason.</summary>
    public static (bool Ok, string Text) ParseGeminiReply(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
                return (false, err.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } msg ? Shorten(msg) : "Gemini menolak permintaan.");
            if (root.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0
                && cands[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
            {
                var sb = new StringBuilder();
                foreach (var p in parts.EnumerateArray())
                    if (p.TryGetProperty("text", out var t) && t.GetString() is { } s) sb.Append(s);
                var text = sb.ToString().Trim();
                if (text.Length > 0) return (true, text);
            }
            if (root.TryGetProperty("promptFeedback", out var fb) && fb.TryGetProperty("blockReason", out _))
                return (false, "Pertanyaan ini diblokir oleh Gemini.");
            return (false, "Gemini tidak memberi jawaban.");
        }
        catch (JsonException) { return (false, "Balasan Gemini tidak terbaca."); }
    }

    static string Shorten(string s) => s.Length <= 160 ? s : s[..160] + "…";
}
