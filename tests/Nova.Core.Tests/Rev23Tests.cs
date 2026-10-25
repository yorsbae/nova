using Nova.Core.Configuration;
using Nova.Core.Dock;
using Nova.Core.Media;
using Nova.Core.Notifications;
using Xunit;

namespace Nova.Core.Tests;

public class DockMagnifyTests
{
    [Fact]
    public void Icon_under_the_pointer_gets_the_full_scale()
        => Assert.Equal(1.7, DockMagnify.Scale(100, 100, 40, 1.7), 6);

    [Fact]
    public void Scale_falls_off_smoothly_and_never_drops_below_one()
    {
        var near = DockMagnify.Scale(100, 120, 40, 1.7);
        var far = DockMagnify.Scale(100, 400, 40, 1.7);
        Assert.InRange(near, 1.01, 1.69);
        Assert.InRange(far, 1.0, 1.0001);
        Assert.True(DockMagnify.Scale(100, 110, 40, 1.7) > DockMagnify.Scale(100, 140, 40, 1.7));
    }

    [Theory]
    [InlineData(double.NaN, 100, 40, 1.7)]
    [InlineData(100, double.NaN, 40, 1.7)]
    [InlineData(100, 100, 0, 1.7)]
    [InlineData(100, 100, 40, 1.0)]
    [InlineData(100, 100, 40, 0.5)]
    [InlineData(double.PositiveInfinity, 100, 40, 1.7)]
    public void Bad_input_means_no_zoom(double p, double c, double sigma, double max)
        => Assert.Equal(1.0, DockMagnify.Scale(p, c, sigma, max));

    [Fact]
    public void Scale_is_capped()
        => Assert.Equal(DockMagnify.MaxAllowedScale, DockMagnify.Scale(0, 0, 40, 9), 6);

    [Fact]
    public void Headroom_fits_the_biggest_icon()
        => Assert.True(DockMagnify.HeadroomFor(26, 1.7) >= 26 * 0.7);

    [Fact]
    public void Approach_converges_and_snaps()
    {
        double v = 1;
        for (int i = 0; i < 80; i++) v = DockMagnify.Approach(v, 1.7);
        Assert.Equal(1.7, v);
    }
}

public class AppAlertsTests
{
    [Theory]
    [InlineData("(3) WhatsApp - Brave", 3)]
    [InlineData("(12) WhatsApp", 12)]
    [InlineData("[7] Telegram Web", 7)]
    [InlineData("(0) WhatsApp", null)]
    [InlineData("WhatsApp", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("(99999) WhatsApp", null)]
    public void Reads_the_unread_counter(string? title, int? expected)
        => Assert.Equal(expected, AppAlerts.UnreadFromTitle(title));

    [Theory]
    [InlineData("WhatsApp.exe", "WhatsApp", "WhatsApp")]
    [InlineData("WhatsApp.Root", "", "WhatsApp")]
    [InlineData("Telegram", "Telegram", "Telegram")]
    [InlineData("brave", "(2) WhatsApp - Brave", "WhatsApp")]
    [InlineData("brave", "WhatsApp - Brave", "WhatsApp")]
    [InlineData("Discord", "", "Discord")]
    public void Identifies_chat_apps(string proc, string title, string expected)
        => Assert.Equal(expected, AppAlerts.Identify(proc, title)?.Name);

    [Theory]
    [InlineData("chrome", "Budget - Google Sheets")]
    [InlineData("notepad", "notes.txt")]
    [InlineData("online", "x")]       // "line" is matched exactly, not as a substring
    public void Other_programs_are_ignored(string proc, string title)
        => Assert.Null(AppAlerts.Identify(proc, title));

    [Fact]
    public void Describe_mentions_the_count_when_known()
    {
        var wa = AppAlerts.Identify("whatsapp", "")!;
        Assert.Equal("WhatsApp · 3 belum dibaca", AppAlerts.Describe(wa, 3));
        Assert.Equal("WhatsApp · ada pesan baru", AppAlerts.Describe(wa, null));
    }
}

public class TrackNameTests
{
    [Theory]
    [InlineData("Superman Is Dead - Ketika Senja (Lyric Video)", "Superman Is Dead", "Ketika Senja")]
    [InlineData("Ketika Senja [Official Music Video]", "Superman Is Dead", "Ketika Senja")]
    [InlineData("Ketika Senja - Superman Is Dead", "Superman Is Dead", "Ketika Senja")]
    [InlineData("Ketika Senja", "Superman Is Dead", "Ketika Senja")]
    [InlineData("Song (Live at Wembley)", "Band", "Song (Live at Wembley)")]   // not noise: kept
    [InlineData("Band - ", "Band", "Band -")]                                     // never empty
    public void Cleans_titles(string title, string artist, string expected)
        => Assert.Equal(expected, TrackName.CleanTitle(title, artist));

    [Fact]
    public void Topic_channels_lose_the_suffix()
        => Assert.Equal("Dewa 19", TrackName.CleanArtist("Dewa 19 - Topic"));
}

public class Rev23SettingsTests
{
    [Fact]
    public void New_defaults_are_island_only_small_dock_with_zoom()
    {
        var s = new NovaSettings();
        Assert.False(s.ShowTopBar);
        Assert.True(s.ReserveTopSpace);
        Assert.True(s.DockMagnify);
        Assert.Equal(26, s.DockIconSize);
        Assert.False(s.LyricsEnabled);   // sends data off the PC: stays opt-in
    }

    [Fact]
    public void Old_default_icon_size_shrinks_once_but_chosen_sizes_stay()
    {
        var old = SettingsValidator.Normalize(new NovaSettings { SchemaVersion = 4, DockIconSize = 32, DockSpacing = 6 });
        Assert.Equal(26, old.DockIconSize);
        var custom = SettingsValidator.Normalize(new NovaSettings { SchemaVersion = 4, DockIconSize = 40, DockSpacing = 6 });
        Assert.Equal(40, custom.DockIconSize);
        var chosenLater = SettingsValidator.Normalize(new NovaSettings { SchemaVersion = 5, DockIconSize = 32, DockSpacing = 6 });
        Assert.Equal(32, chosenLater.DockIconSize);
    }

    [Fact]
    public void Magnify_scale_is_clamped()
    {
        Assert.Equal(2.2, SettingsValidator.Normalize(new NovaSettings { DockMagnifyScale = 9 }).DockMagnifyScale);
        Assert.Equal(1.0, SettingsValidator.Normalize(new NovaSettings { DockMagnifyScale = 0.2 }).DockMagnifyScale);
        Assert.Equal(1.7, SettingsValidator.Normalize(new NovaSettings { DockMagnifyScale = double.NaN }).DockMagnifyScale);
    }

    [Fact]
    public void Island_strip_covers_island_and_gap()
        => Assert.Equal(NotchLayout.BarHeightDip(18) + 8 + 4, NotchLayout.IslandStripDip(18, 8));
}
