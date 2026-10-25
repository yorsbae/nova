using Nova.Core.Clipboard;
using Nova.Core.Configuration;
using Nova.Core.Media;
using Nova.Core.Status;
using Nova.Core.Weather;
using Xunit;

namespace Nova.Core.Tests;

public class StatusTests
{
    [Fact]
    public void Cpu_usage_from_two_samples()
    {
        // kernel includes idle: total = 1000 (kernel) + 500 (user) = 1500; idle 900 -> busy 600 -> 40%
        Assert.Equal(40, CpuUsage.Compute(0, 0, 0, 900, 1000, 500));
    }

    [Fact]
    public void Cpu_usage_rejects_no_elapsed_time_and_backwards_counters()
    {
        Assert.Null(CpuUsage.Compute(10, 20, 30, 10, 20, 30));
        Assert.Null(CpuUsage.Compute(100, 100, 100, 50, 200, 200));
    }

    [Fact]
    public void Memory_percent()
    {
        Assert.Equal(75, MemoryUsage.UsedPercent(1000, 250));
        Assert.Null(MemoryUsage.UsedPercent(0, 0));
        Assert.Null(MemoryUsage.UsedPercent(100, 200));
    }

    [Theory]
    [InlineData(1, 128, 255)]   // desktop, no battery
    [InlineData(0, 255, 50)]    // unknown
    [InlineData(1, 8, 101)]     // invalid percent
    public void Battery_absent_or_unknown_is_null(int ac, int flag, int pct)
        => Assert.Null(BatteryInfo.From((byte)ac, (byte)flag, (byte)pct));

    [Fact]
    public void Battery_states_are_text_labelled()
    {
        var charging = BatteryInfo.From(1, 8, 80)!;
        Assert.True(charging.Charging);
        Assert.Equal("Battery 80% (charging)", StatusFormatter.Format(null, null, charging));
        Assert.Equal("Battery 100% (plugged in)", StatusFormatter.Format(null, null, BatteryInfo.From(1, 1, 100)));
        Assert.Equal("Battery 40%", StatusFormatter.Format(null, null, BatteryInfo.From(0, 2, 40)));
    }

    [Fact]
    public void Status_line_skips_missing_values()
    {
        Assert.Equal("CPU 12%  ·  Memory 48%", StatusFormatter.Format(12, 48, null));
        Assert.Equal("", StatusFormatter.Format(null, null, null));
    }
}

public class MediaInfoTests
{
    [Fact]
    public void Describe_handles_missing_artist_and_title()
    {
        Assert.Equal("Song — Band", new MediaInfo("Song", "Band", true, true, true, true).Describe());
        Assert.Equal("Song", new MediaInfo("Song", " ", true, true, true, true).Describe());
        Assert.Equal("", new MediaInfo("", "Band", true, true, true, true).Describe());
    }

    [Fact]
    public void Detects_track_change()
    {
        var a = new MediaInfo("A", "X", true, true, true, true);
        Assert.False(a.IsDifferentTrack(a with { IsPlaying = false }));
        Assert.True(a.IsDifferentTrack(a with { Title = "B" }));
        Assert.True(a.IsDifferentTrack(null));
    }
}

public class WeatherTests
{
    [Fact]
    public void Parses_current_conditions()
    {
        const string json = """{"latitude":-6.2,"longitude":106.8,"current":{"time":"2026-10-24T10:00","interval":900,"temperature_2m":29.4,"weather_code":2}}""";
        Assert.True(WeatherApi.TryParseCurrent(json, out var r));
        Assert.Equal(29.4, r!.TemperatureC);
        Assert.Equal(2, r.Code);
    }

    [Theory]
    [InlineData("")] [InlineData("not json")] [InlineData("{}")] [InlineData("""{"current":{"temperature_2m":"warm","weather_code":1}}""")]
    public void Rejects_bad_weather_payloads(string json) => Assert.False(WeatherApi.TryParseCurrent(json, out _));

    [Fact]
    public void Parses_geocoding_result()
    {
        const string json = """{"results":[{"id":1,"name":"Semarang","latitude":-6.99,"longitude":110.42,"country":"Indonesia"}]}""";
        Assert.True(WeatherApi.TryParseGeocode(json, out var g));
        Assert.Equal("Semarang, Indonesia", g!.DisplayName);
        Assert.Equal(-6.99, g.Latitude);
    }

    [Theory]
    [InlineData("{}")] [InlineData("""{"results":[]}""")] [InlineData("""{"results":[{"name":"X","latitude":999,"longitude":0}]}""")]
    public void Rejects_bad_geocoding(string json) => Assert.False(WeatherApi.TryParseGeocode(json, out _));

    [Fact]
    public void Formats_units_and_descriptions()
    {
        var r = new WeatherReading(20, 61);
        Assert.Equal("Home: 20°C, Rain", WeatherApi.Format(r, false, "Home"));
        Assert.Equal("68°F, Rain", WeatherApi.Format(r, true, ""));
        Assert.Equal("Unknown conditions", WeatherApi.Describe(1234));
    }

    [Fact]
    public void Urls_use_invariant_culture_and_escape()
    {
        Assert.Equal("https://api.open-meteo.com/v1/forecast?latitude=-6.99&longitude=110.42&current=temperature_2m,weather_code",
                     WeatherApi.ForecastUrl(-6.99, 110.42));
        Assert.Contains("name=New%20York", WeatherApi.GeocodeUrl(" New York "));
    }
}

public class ClipboardHistoryTests
{
    [Fact]
    public void Newest_first_with_dedupe_to_top()
    {
        var h = new ClipboardHistory(5);
        h.Add("a"); h.Add("b"); h.Add("a");
        Assert.Equal(new[] { "a", "b" }, h.Items);
    }

    [Fact]
    public void Caps_count_and_drops_oldest()
    {
        var h = new ClipboardHistory(3);
        foreach (var s in new[] { "1", "2", "3", "4" }) h.Add(s);
        Assert.Equal(new[] { "4", "3", "2" }, h.Items);
        h.MaxItems = 1;
        Assert.Equal(new[] { "4" }, h.Items);
    }

    [Fact]
    public void Ignores_blank_and_oversized_text()
    {
        var h = new ClipboardHistory();
        Assert.False(h.Add("   "));
        Assert.False(h.Add(null));
        Assert.False(h.Add(new string('x', ClipboardHistory.MaxEntryLength + 1)));
        Assert.Empty(h.Items);
    }

    [Fact]
    public void Clear_empties_and_raises_changed_once()
    {
        var h = new ClipboardHistory();
        h.Add("secret");
        int n = 0; h.Changed += () => n++;
        h.Clear(); h.Clear();
        Assert.Empty(h.Items);
        Assert.Equal(1, n);
    }

    [Fact]
    public void Preview_collapses_whitespace_and_truncates()
    {
        Assert.Equal("a b c", ClipboardHistory.Preview("a\n  b\t c"));
        var p = ClipboardHistory.Preview(new string('x', 100), 10);
        Assert.Equal(10, p.Length);
        Assert.EndsWith("…", p);
    }
}

public class ProfileTests
{
    [Fact]
    public void Save_apply_roundtrip_leaves_user_choices_alone()
    {
        var s = new NovaSettings { DockIconSize = 64, Theme = ThemeMode.Dark, Hotkey = "Ctrl+Shift+K", ReducedMotion = true };
        s.DockItems.Add(new DockItem { Name = "n", Path = @"C:\n.exe" });
        Assert.True(LayoutProfiles.TrySave(s, "  Work ", out _));
        s.DockIconSize = 30; s.Theme = ThemeMode.Light;

        Assert.True(LayoutProfiles.TryApply(s, "work"));        // case-insensitive
        Assert.Equal(64, s.DockIconSize);
        Assert.Equal(ThemeMode.Dark, s.Theme);
        Assert.Equal("Ctrl+Shift+K", s.Hotkey);
        Assert.True(s.ReducedMotion);
        Assert.Single(s.DockItems);
        Assert.Equal(LayoutPreset.Custom, s.Preset);
    }

    [Fact]
    public void Saving_same_name_replaces_and_limit_is_enforced()
    {
        var s = new NovaSettings();
        LayoutProfiles.TrySave(s, "A", out _);
        s.DockSpacing = 20;
        LayoutProfiles.TrySave(s, "a", out _);
        Assert.Single(s.Profiles);
        Assert.Equal(20, s.Profiles[0].DockSpacing);

        for (int i = 0; i < SettingsValidator.MaxProfiles - 1; i++) Assert.True(LayoutProfiles.TrySave(s, "P" + i, out _));
        Assert.False(LayoutProfiles.TrySave(s, "one too many", out var err));
        Assert.NotNull(err);
    }

    [Theory]
    [InlineData("")] [InlineData("   ")] [InlineData("bad\nname")]
    public void Rejects_invalid_names(string name) => Assert.False(LayoutProfiles.TrySave(new NovaSettings(), name, out _));

    [Fact]
    public void Delete_and_apply_unknown()
    {
        var s = new NovaSettings();
        LayoutProfiles.TrySave(s, "X", out _);
        Assert.False(LayoutProfiles.TryApply(s, "nope"));
        Assert.True(LayoutProfiles.Delete(s, "x"));
        Assert.False(LayoutProfiles.Delete(s, "x"));
    }

    [Fact]
    public void Validator_repairs_profiles_from_hand_edited_json()
    {
        var s = new NovaSettings();
        s.Profiles.Add(new LayoutProfile { Name = "ok", DockIconSize = 9999, AccentColor = "nope" });
        s.Profiles.Add(new LayoutProfile { Name = "OK" });           // duplicate
        s.Profiles.Add(new LayoutProfile { Name = "" });             // blank
        SettingsValidator.Normalize(s);
        Assert.Single(s.Profiles);
        Assert.Equal(96, s.Profiles[0].DockIconSize);
        Assert.Equal("#4C8DFF", s.Profiles[0].AccentColor);
    }
}

public class ValidatorV2Tests
{
    [Fact]
    public void Repairs_new_fields()
    {
        var s = new NovaSettings
        {
            ClipboardMaxItems = 5000, WeatherCity = "  Semarang ", WeatherLatitude = 500, WeatherLongitude = 10,
            IslandMonitor = "\\\\.\\DISPLAY2\u0007"
        };
        SettingsValidator.Normalize(s);
        Assert.Equal(50, s.ClipboardMaxItems);
        Assert.Equal("Semarang", s.WeatherCity);
        Assert.True(double.IsNaN(s.WeatherLatitude));
        Assert.True(double.IsNaN(s.WeatherLongitude));
        Assert.Equal("\\\\.\\DISPLAY2", s.IslandMonitor);
    }

    [Fact]
    public void Clearing_city_clears_resolved_location()
    {
        var s = new NovaSettings { WeatherCity = "", WeatherPlace = "Old", WeatherLatitude = 1, WeatherLongitude = 2 };
        SettingsValidator.Normalize(s);
        Assert.Equal("", s.WeatherPlace);
        Assert.True(double.IsNaN(s.WeatherLatitude));
    }

    [Fact]
    public void Privacy_features_are_off_by_default()
    {
        var d = new NovaSettings();
        Assert.False(d.WeatherEnabled);
        Assert.False(d.ClipboardHistoryEnabled);
    }
}

public class BackupTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "nova-backup-" + Guid.NewGuid());
    public BackupTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Save_keeps_previous_generation_as_bak()
    {
        var store = new SettingsStore(Path.Combine(_dir, "s.json"));
        store.Save(new NovaSettings { DockIconSize = 50 });
        store.Save(new NovaSettings { DockIconSize = 70 });
        Assert.True(File.Exists(store.BackupPath));
        Assert.True(SettingsStore.TryImport(store.BackupPath, out var old, out _));
        Assert.Equal(50, old!.DockIconSize);
        Assert.Equal(70, store.Load().Settings.DockIconSize);
    }

    [Fact]
    public void Export_then_import_roundtrips_including_nan_coordinates_and_profiles()
    {
        var s = new NovaSettings { Theme = ThemeMode.Dark, ClipboardHistoryEnabled = true };
        LayoutProfiles.TrySave(s, "Work", out _);
        var file = Path.Combine(_dir, "backup.json");
        Assert.Null(SettingsStore.Export(file, s));

        Assert.True(SettingsStore.TryImport(file, out var r, out var err), err);
        Assert.Equal(ThemeMode.Dark, r!.Theme);
        Assert.True(r.ClipboardHistoryEnabled);
        Assert.Single(r.Profiles);
        Assert.True(double.IsNaN(r.WeatherLatitude));
    }

    [Fact]
    public void Import_rejects_garbage_missing_and_huge_files_and_normalizes_values()
    {
        var bad = Path.Combine(_dir, "bad.json"); File.WriteAllText(bad, "<html>");
        Assert.False(SettingsStore.TryImport(bad, out _, out var e1)); Assert.NotNull(e1);
        Assert.False(SettingsStore.TryImport(Path.Combine(_dir, "nope.json"), out _, out _));

        var huge = Path.Combine(_dir, "huge.json");
        File.WriteAllText(huge, new string(' ', (int)SettingsStore.MaxImportBytes + 10));
        Assert.False(SettingsStore.TryImport(huge, out _, out _));

        var wild = Path.Combine(_dir, "wild.json"); File.WriteAllText(wild, """{"dockIconSize": 5000, "hotkey": "oops"}""");
        Assert.True(SettingsStore.TryImport(wild, out var w, out _));
        Assert.Equal(96, w!.DockIconSize);
        Assert.Equal("Ctrl+Alt+N", w.Hotkey);
    }
}
