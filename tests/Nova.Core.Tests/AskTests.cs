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

    [Fact]
    public void Answer_is_cleaned_of_menus_question_and_footer()
    {
        var raw = "AI Mode\nSemua\nGambar\napa itu wpf\nWPF adalah kerangka UI dari Microsoft.\n\nDipakai untuk aplikasi desktop Windows.\nTampilkan lebih banyak\nTanya apa saja\nsampah";
        var clean = AskSearch.CleanAnswer(raw, "apa itu wpf");
        Assert.Equal("WPF adalah kerangka UI dari Microsoft.\n\nDipakai untuk aplikasi desktop Windows.", clean);
    }

    [Fact]
    public void Blocked_pages_are_detected_and_prefix_a_picks_ai_here()
    {
        Assert.True(AskSearch.LooksBlocked("https://www.google.com/sorry/index?x=1", ""));
        Assert.False(AskSearch.LooksBlocked("https://www.google.com/search?udm=50", "halo"));
        Assert.Equal((AskEngine.AiHere, "kapan magrib"), AskSearch.ParsePrefix("a kapan magrib", AskEngine.Google));
        Assert.True(AskSearch.TryParseEngine("aihere", out var e) && e == AskEngine.AiHere);
    }
}

public class TranslationTests
{
    [Fact]
    public void Url_autodetects_source_and_escapes_text()
    {
        Assert.Equal("https://api.mymemory.translated.net/get?q=good%20morning&langpair=Autodetect%7Cid", Nova.Core.Translate.Translation.BuildUrl("good morning", "id"));
        Assert.StartsWith("https://translate.google.com/?sl=auto&tl=ja&op=translate&text=a%20%26%20b", Nova.Core.Translate.Translation.BuildBrowserUrl("a & b", "ja"));
    }

    [Fact]
    public void Reply_is_parsed_decoded_and_errors_reported()
    {
        var ok = Nova.Core.Translate.Translation.ParseReply("{\"responseData\":{\"translatedText\":\"Selamat pagi, it&#39;s ok\"},\"responseStatus\":200}");
        Assert.True(ok.Ok); Assert.Equal("Selamat pagi, it's ok", ok.Text);
        var quota = Nova.Core.Translate.Translation.ParseReply("{\"responseData\":{\"translatedText\":\"MYMEMORY WARNING\"},\"responseStatus\":\"429\"}");
        Assert.False(quota.Ok); Assert.Contains("Ctrl+Enter", quota.Text);
        Assert.False(Nova.Core.Translate.Translation.ParseReply("<html>").Ok);
    }

    [Fact]
    public void Length_limit_counts_bytes_and_settings_are_cleaned()
    {
        Assert.True(Nova.Core.Translate.Translation.FitsLimit(new string('a', 500)));
        Assert.False(Nova.Core.Translate.Translation.FitsLimit(new string('日', 200)));   // 600 bytes
        var s = Nova.Core.Configuration.SettingsValidator.Normalize(new Nova.Core.Configuration.NovaSettings { TranslateTarget = "xx", TranslateHotkey = "Ctrl+Alt+K" });
        Assert.Equal("id", s.TranslateTarget);
        Assert.NotEqual("Ctrl+Alt+K", s.TranslateHotkey);   // clashes with the Tanya shortcut
    }
}

public class AskStyleTests
{
    [Fact]
    public void Short_prefix_style_and_truncation()
    {
        Assert.True(AskSearch.StripShort("s apa itu wpf", out var rest)); Assert.Equal("apa itu wpf", rest);
        Assert.False(AskSearch.StripShort("sapi makan apa", out _));
        Assert.False(AskSearch.StripShort("s", out _));
        Assert.Equal("apa itu wpf. Jawab dalam satu kalimat saja.", AskSearch.ApplyStyle("apa itu wpf?", AskSearch.ShortStyle));
        Assert.Equal("apa itu wpf", AskSearch.ApplyStyle("apa itu wpf", ""));
        Assert.Equal("abc…", AskSearch.Truncate("abcdef", 3));
    }

    [Fact]
    public void Style_goes_into_gemini_instruction_and_settings_are_clamped()
    {
        Assert.Contains("maksimal 3 kalimat", AskSearch.BuildGeminiRequest(new[] { new AskTurn(true, "x") }, "maksimal 3 kalimat"));
        Assert.Contains("langsung ke inti", AskSearch.BuildGeminiRequest(new[] { new AskTurn(true, "x") }));
        var s = Nova.Core.Configuration.SettingsValidator.Normalize(new Nova.Core.Configuration.NovaSettings { AskStyle = "  ", AskMaxChars = 5 });
        Assert.Equal(AskSearch.DefaultStyle, s.AskStyle);
        Assert.Equal(100, s.AskMaxChars);
    }
}

public class TranslationVersionTests
{
    const string Json = "{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"Selamat pagi\"},\"matches\":[" +
        "{\"translation\":\"Selamat pagi\"},{\"translation\":\"Pagi yang baik\"},{\"translation\":\"good morning\"},{\"translation\":\"Met pagi\"},{\"translation\":\"Pagi\"}]}";

    [Fact]
    public void Versions_are_distinct_ordered_and_limited()
    {
        var (ok, v, _) = Nova.Core.Translate.Translation.ParseVersions(Json, 3, "good morning");
        Assert.True(ok);
        Assert.Equal(new[] { "Selamat pagi", "Pagi yang baik", "Met pagi" }, v);   // duplicate and the untranslated source are skipped
    }

    [Fact]
    public void Single_unchanged_result_is_still_returned_and_errors_pass_through()
    {
        var same = "{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"OK\"}}";
        var (ok, v, _) = Nova.Core.Translate.Translation.ParseVersions(same, 3, "OK");
        Assert.True(ok); Assert.Single(v);
        Assert.False(Nova.Core.Translate.Translation.ParseVersions("{\"responseStatus\":\"429\"}", 3, "x").Ok);
        Assert.Equal(1, Nova.Core.Configuration.SettingsValidator.Normalize(new Nova.Core.Configuration.NovaSettings { TranslateVersions = 0 }).TranslateVersions);
    }
}
