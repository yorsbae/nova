using Nova.Core.Ask;
using Nova.Core.Configuration;
using Xunit;

namespace Nova.Core.Tests;

public class AskSearchTests
{
    [Theory]
    [InlineData("b cuaca jogja", AskEngine.Brave, "cuaca jogja")]
    [InlineData("g cuaca jogja", AskEngine.Google, "cuaca jogja")]
    [InlineData("ai apa itu wpf", AskEngine.GoogleAi, "apa itu wpf")]
    [InlineData("? apa itu wpf", AskEngine.Gemini, "apa itu wpf")]
    [InlineData("budi pergi ke pasar", AskEngine.Google, "budi pergi ke pasar")]   // "b" must be a whole word: fallback is kept
    [InlineData("b", AskEngine.Google, "b")]                                       // nothing after the prefix: it is just the query
    public void Prefix_picks_engine_only_for_whole_word(string input, AskEngine expected, string query)
    {
        var (engine, q) = AskSearch.ParsePrefix(input, AskEngine.Google);
        Assert.Equal(expected, engine);
        Assert.Equal(query, q);
    }

    [Fact]
    public void Urls_are_escaped_and_ai_mode_uses_udm50()
    {
        Assert.Equal("https://search.brave.com/search?q=a%20%26%20b", AskSearch.BuildUrl(AskEngine.Brave, "a & b"));
        Assert.Equal("https://www.google.com/search?q=halo", AskSearch.BuildUrl(AskEngine.Google, "halo"));
        Assert.Equal("https://www.google.com/search?udm=50&q=halo", AskSearch.BuildUrl(AskEngine.GoogleAi, "halo"));
    }

    [Fact]
    public void Gemini_reply_is_parsed_and_errors_are_reported()
    {
        var ok = AskSearch.ParseGeminiReply("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Halo\"},{\"text\":\" dunia\"}]}}]}");
        Assert.True(ok.Ok); Assert.Equal("Halo dunia", ok.Text);
        var bad = AskSearch.ParseGeminiReply("{\"error\":{\"message\":\"API key not valid\"}}");
        Assert.False(bad.Ok); Assert.Contains("API key", bad.Text);
        Assert.False(AskSearch.ParseGeminiReply("not json").Ok);
    }

    [Fact]
    public void Gemini_request_keeps_only_recent_turns()
    {
        var turns = Enumerable.Range(0, 20).Select(i => new AskTurn(i % 2 == 0, "t" + i)).ToList();
        var json = AskSearch.BuildGeminiRequest(turns);
        Assert.DoesNotContain("\"t0\"", json);
        Assert.Contains("\"t19\"", json);
        Assert.Contains("\"role\":\"model\"", json);
    }

    [Fact]
    public void Settings_validation_cleans_ask_fields()
    {
        var s = SettingsValidator.Normalize(new NovaSettings { AskEngine = "bing", GeminiApiKey = "bad key", GeminiModel = "../x", AskHotkey = "Ctrl+Alt+Space" });
        Assert.Equal("googleai", s.AskEngine);
        Assert.Equal("", s.GeminiApiKey);
        Assert.Equal(AskSearch.DefaultModel, s.GeminiModel);
        Assert.NotEqual("Ctrl+Alt+Space", s.AskHotkey);   // clashes with the launcher shortcut
    }
}
