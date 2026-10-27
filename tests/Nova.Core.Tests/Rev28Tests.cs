using Nova.Core.Configuration;
using Nova.Core.Launch;
using Xunit;

namespace Nova.Core.Tests;

public class LauncherSearchTests
{
    static LauncherEntry E(string name, string path = "") => new(name, string.IsNullOrEmpty(path) ? @"C:\x\" + name + ".lnk" : path);

    [Fact]
    public void Exact_beats_prefix_beats_word_beats_contains_beats_letters()
    {
        int exact = LauncherSearch.Score("code", "Code");
        int prefix = LauncherSearch.Score("code", "Code Editor");
        int word = LauncherSearch.Score("code", "Visual Studio Code");
        int contains = LauncherSearch.Score("code", "Barcode Tool");
        int letters = LauncherSearch.Score("vsc", "Visual Studio Code");
        Assert.True(exact > prefix && prefix > word && word > contains && contains > letters && letters > 0);
    }

    [Fact]
    public void No_match_scores_zero_and_empty_query_matches_nothing()
    {
        Assert.Equal(0, LauncherSearch.Score("zzz", "Notepad"));
        Assert.Equal(0, LauncherSearch.Score("", "Notepad"));
        Assert.Empty(LauncherSearch.Rank(new[] { E("Notepad") }, "  "));
    }

    [Fact]
    public void Match_is_case_insensitive()
        => Assert.Equal(LauncherSearch.Score("NOTE", "notepad"), LauncherSearch.Score("note", "NotePad"));

    [Fact]
    public void Rank_orders_best_first_and_removes_duplicate_paths()
    {
        var list = new[] { E("Chrome Remote", @"C:\a.lnk"), E("Chrome", @"C:\b.lnk"), E("Chrome", @"C:\B.LNK"), E("Notepad", @"C:\c.lnk") };
        var r = LauncherSearch.Rank(list, "chrome");
        Assert.Equal(new[] { @"C:\b.lnk", @"C:\a.lnk" }, r.Select(e => e.Path).ToArray());
    }

    [Fact]
    public void Rank_respects_the_limit()
    {
        var list = Enumerable.Range(0, 20).Select(i => E("App" + i)).ToList();
        Assert.Equal(5, LauncherSearch.Rank(list, "app", 5).Count);
    }

    [Theory]
    [InlineData("2+3", "5")]
    [InlineData("= 12*(3+4)", "84")]
    [InlineData("10/4", "2.5")]
    [InlineData("2^3^2", "512")]
    [InlineData("-3+5", "2")]
    [InlineData("7%4", "3")]
    [InlineData("1,5*2", "3")]
    [InlineData("0.1+0.2", "0.3")]
    public void Arithmetic_is_evaluated(string text, string expected)
    {
        Assert.True(LauncherSearch.TryEvaluate(text, out var r));
        Assert.Equal(expected, r);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("")]
    [InlineData("1/0")]
    [InlineData("2+")]
    [InlineData("(1+2")]
    [InlineData("1+2)")]
    [InlineData("calc.exe")]
    [InlineData("2+abc")]
    [InlineData("Math.Sqrt(4)*2")]
    public void Anything_that_is_not_a_clean_sum_is_rejected(string text)
        => Assert.False(LauncherSearch.TryEvaluate(text, out _));

    [Theory]
    [InlineData("https://example.com/a?b=1", true)]
    [InlineData("http://example.org", true)]
    [InlineData("www.example.com", true)]
    [InlineData("file:///C:/Windows/system32/cmd.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("localhost", false)]
    [InlineData("two words.com", false)]
    [InlineData("notepad", false)]
    public void Only_http_addresses_are_web_addresses(string text, bool expected)
        => Assert.Equal(expected, LauncherSearch.TryGetWebAddress(text, out _));

    [Fact]
    public void Www_gets_https()
    {
        Assert.True(LauncherSearch.TryGetWebAddress("www.example.com", out var url));
        Assert.StartsWith("https://www.example.com", url);
    }
}

public class LauncherSettingsTests
{
    [Fact]
    public void Defaults()
    {
        var s = new NovaSettings();
        Assert.Equal("Ctrl+Alt+Space", s.LauncherHotkey);
        Assert.True(s.IslandEyeFollowMouse);
        Assert.True(s.IslandEyeMoods);
        Assert.False(s.IslandEyeAutoTheme);
    }

    [Fact]
    public void Invalid_launcher_shortcut_falls_back_and_empty_means_off()
    {
        var bad = new NovaSettings { LauncherHotkey = "nope" };
        SettingsValidator.Normalize(bad);
        Assert.Equal("Ctrl+Alt+Space", bad.LauncherHotkey);

        var off = new NovaSettings { LauncherHotkey = "" };
        SettingsValidator.Normalize(off);
        Assert.Equal("", off.LauncherHotkey);
    }

    [Fact]
    public void Launcher_shortcut_cannot_duplicate_the_other_shortcuts()
    {
        var s = new NovaSettings { Hotkey = "Ctrl+Alt+K", ToggleHotkey = "Ctrl+Alt+J", LauncherHotkey = "ctrl+alt+j" };
        SettingsValidator.Normalize(s);
        Assert.Equal("Ctrl+Alt+Space", s.LauncherHotkey);

        var taken = new NovaSettings { Hotkey = "Ctrl+Alt+Space", LauncherHotkey = "Ctrl+Alt+Space" };
        SettingsValidator.Normalize(taken);
        Assert.Equal("", taken.LauncherHotkey);
    }
}
