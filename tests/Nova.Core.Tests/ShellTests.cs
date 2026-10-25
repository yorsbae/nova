using Nova.Core.Configuration;
using Nova.Core.Dock;
using Nova.Core.Media;
using Nova.Core.Status;
using Xunit;

namespace Nova.Core.Tests;

public class LrcTests
{
    [Fact]
    public void Parses_timestamps_and_ignores_tags_and_plain_lines()
    {
        var lines = Lrc.Parse("[ar:Someone]\n[00:12.50] first\nplain line\n[01:02.5]second\r\n");
        Assert.Equal(2, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(12.5), lines[0].Time);
        Assert.Equal("first", lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(62.5), lines[1].Time);
    }

    [Fact]
    public void A_line_with_several_stamps_is_repeated_and_sorted()
    {
        var lines = Lrc.Parse("[00:30.00][00:10.00] chorus");
        Assert.Equal(2, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), lines[0].Time);
        Assert.All(lines, l => Assert.Equal("chorus", l.Text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no stamps at all")]
    public void Empty_or_unsynced_input_gives_no_lines(string? text) => Assert.Empty(Lrc.Parse(text));

    [Fact]
    public void IndexAt_finds_the_current_line()
    {
        var lines = Lrc.Parse("[00:10.00]a\n[00:20.00]b\n[00:30.00]c");
        Assert.Equal(-1, Lrc.IndexAt(lines, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, Lrc.IndexAt(lines, TimeSpan.FromSeconds(10)));
        Assert.Equal(1, Lrc.IndexAt(lines, TimeSpan.FromSeconds(25)));
        Assert.Equal(2, Lrc.IndexAt(lines, TimeSpan.FromSeconds(999)));
    }
}

public class MediaTimelineTests
{
    [Fact]
    public void Position_advances_only_while_playing_and_is_clamped()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var playing = new MediaTimeline(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(100), t0, true);
        var paused = playing with { IsPlaying = false };
        Assert.Equal(TimeSpan.FromSeconds(15), playing.Now(t0.AddSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(10), paused.Now(t0.AddSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(100), playing.Now(t0.AddMinutes(10)));
        Assert.Equal(0.5, playing.Fraction(t0.AddSeconds(40)), 3);
    }

    [Fact]
    public void Unknown_length_gives_zero_progress()
        => Assert.Equal(0, new MediaTimeline(TimeSpan.FromSeconds(5), TimeSpan.Zero, DateTime.UtcNow, true).Fraction(DateTime.UtcNow));
}

public class StatusGlyphTests
{
    [Fact]
    public void Volume_glyph_follows_level_and_mute()
    {
        Assert.Equal(StatusGlyphs.Mute, StatusGlyphs.Volume(0.8, muted: true));
        Assert.Equal("\uE992", StatusGlyphs.Volume(0, false));
        Assert.Equal("\uE993", StatusGlyphs.Volume(0.2, false));
        Assert.Equal("\uE994", StatusGlyphs.Volume(0.5, false));
        Assert.Equal("\uE995", StatusGlyphs.Volume(1, false));
    }

    [Theory]
    [InlineData(0, "\uE850")]
    [InlineData(55, "\uE855")]
    [InlineData(94, "\uE859")]
    [InlineData(100, "\uE83F")]
    [InlineData(-5, "\uE850")]
    public void Battery_glyph_is_bucketed_by_ten_percent(int percent, string expected)
        => Assert.Equal(expected, StatusGlyphs.Battery(percent, charging: false));

    [Fact]
    public void Charging_has_its_own_glyph() => Assert.Equal(StatusGlyphs.Charging, StatusGlyphs.Battery(40, true));

    [Fact]
    public void Wifi_strength_is_monotonic()
    {
        Assert.Equal(1.0, StatusGlyphs.WifiOpacity(-1));
        Assert.True(StatusGlyphs.WifiOpacity(90) >= StatusGlyphs.WifiOpacity(50));
        Assert.True(StatusGlyphs.WifiOpacity(50) >= StatusGlyphs.WifiOpacity(10));
    }
}

public class DockLayoutTests
{
    [Fact]
    public void Icons_keep_their_size_when_they_fit()
        => Assert.Equal(32, DockLayout.FitIconSize(32, 8, 6, 1920));

    [Fact]
    public void Icons_shrink_so_the_dock_fits_the_screen()
    {
        var icon = DockLayout.FitIconSize(48, 30, 6, 1000);
        Assert.True(icon < 48);
        Assert.True(DockLayout.WidthFor(icon, 30, 6) <= 1000 || icon == DockLayout.MinIconSize);
    }

    [Fact]
    public void Icons_never_go_below_the_minimum()
        => Assert.Equal(DockLayout.MinIconSize, DockLayout.FitIconSize(40, 200, 6, 300));

    [Fact]
    public void Bad_input_falls_back_to_the_requested_size()
        => Assert.Equal(32, DockLayout.FitIconSize(32, 0, 6, double.NaN));
}

public class FileShelfTests
{
    [Fact]
    public void Ignores_duplicates_and_blank_paths_and_respects_the_limit()
    {
        var s = new FileShelf();
        Assert.True(s.Add(@"C:\a.txt"));
        Assert.False(s.Add(@"c:\A.TXT"));
        Assert.False(s.Add("  "));
        for (int i = 0; i < 30; i++) s.Add($@"C:\f{i}.txt");
        Assert.Equal(FileShelf.MaxItems, s.Count);
    }

    [Fact]
    public void Remove_and_clear_work()
    {
        var s = new FileShelf();
        s.Add(@"C:\a.txt"); s.Add(@"C:\b.txt");
        Assert.True(s.Remove(@"C:\A.txt"));
        Assert.False(s.Remove(@"C:\missing.txt"));
        Assert.Single(s.Items);
        s.Clear();
        Assert.Equal(0, s.Count);
    }
}

public class MigrationTests
{
    [Fact]
    public void Old_default_dock_size_moves_to_the_new_default_but_custom_sizes_stay()
    {
        var oldDefaults = SettingsValidator.Normalize(new NovaSettings { SchemaVersion = 2, DockIconSize = 40, DockSpacing = 8, DockBottomOffset = 12 });
        Assert.Equal(new NovaSettings().DockIconSize, oldDefaults.DockIconSize);

        var custom = SettingsValidator.Normalize(new NovaSettings { SchemaVersion = 2, DockIconSize = 48, DockSpacing = 8, DockBottomOffset = 12 });
        Assert.Equal(48, custom.DockIconSize);
    }

    [Fact]
    public void New_features_default_to_the_safe_choice()
    {
        var s = new NovaSettings();
        Assert.False(s.LyricsEnabled);      // sends artist and title to the internet: opt-in only
        Assert.True(s.ShelfEnabled);
        Assert.True(s.DockReservesSpace);
    }
}

public class V22DefaultsTests
{
    [Fact]
    public void Eco_mode_and_activity_alerts_are_on_by_default()
    {
        var s = new Nova.Core.Configuration.NovaSettings();
        Assert.True(s.EcoMode);
        Assert.True(s.ActivityAlerts);
        Assert.True(s.IslandHoverExpand);
    }
}
