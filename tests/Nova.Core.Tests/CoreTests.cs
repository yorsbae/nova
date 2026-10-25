using Nova.Core.Configuration;
using Nova.Core.Launch;
using Xunit;

namespace Nova.Core.Tests;

public class SettingsValidatorTests
{
    [Fact]
    public void Older_files_get_taskbar_replacement_and_notch_switched_on_once()
    {
        var old = new NovaSettings { SchemaVersion = 3, NotchMode = false, HideTaskbar = false, ShowRunningApps = false, DockReservesSpace = false, ShowMedia = false };
        SettingsValidator.Normalize(old);
        Assert.True(old.NotchMode && old.HideTaskbar && old.ShowRunningApps && old.DockReservesSpace && old.ShowMedia);
        Assert.Equal(4, old.SchemaVersion);

        // Once migrated, the user's own choice is kept.
        old.HideTaskbar = false;
        SettingsValidator.Normalize(old);
        Assert.False(old.HideTaskbar);
    }

    [Fact]
    public void Clamps_out_of_range_values()
    {
        var s = new NovaSettings { Opacity = 0.1, DockIconSize = 500, ClockFontSize = double.NaN, DockSpacing = -5 };
        SettingsValidator.Normalize(s);
        Assert.Equal(0.7, s.Opacity);
        Assert.Equal(96, s.DockIconSize);
        Assert.Equal(18, s.ClockFontSize);
        Assert.Equal(0, s.DockSpacing);
    }

    [Fact]
    public void Repairs_invalid_hotkey_accent_and_enums()
    {
        var s = new NovaSettings { Hotkey = "N", AccentColor = "red", Theme = (ThemeMode)99 };
        SettingsValidator.Normalize(s);
        Assert.Equal("Ctrl+Alt+N", s.Hotkey);
        Assert.Equal("#4C8DFF", s.AccentColor);
        Assert.Equal(ThemeMode.System, s.Theme);
    }

    [Fact]
    public void Drops_blank_dock_items_and_caps_count()
    {
        var s = new NovaSettings();
        s.DockItems.Add(new DockItem { Name = "x", Path = "" });
        for (int i = 0; i < 40; i++) s.DockItems.Add(new DockItem { Path = $@"C:\a\app{i}.exe" });
        SettingsValidator.Normalize(s);
        Assert.Equal(SettingsValidator.MaxDockItems, s.DockItems.Count);
        Assert.Equal("app0", s.DockItems[0].Name);
    }

    [Fact]
    public void Null_input_returns_defaults() => Assert.NotNull(SettingsValidator.Normalize(null));
}

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+N", HotkeyModifiers.Control | HotkeyModifiers.Alt, "N")]
    [InlineData("win + shift + F12", HotkeyModifiers.Win | HotkeyModifiers.Shift, "F12")]
    public void Parses_valid(string text, HotkeyModifiers mods, string key)
    {
        Assert.True(HotkeyParser.TryParse(text, out var g));
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(key, g.Key);
    }

    [Theory]
    [InlineData("")] [InlineData(null)] [InlineData("N")] [InlineData("Ctrl+")] [InlineData("Ctrl+A+B")] [InlineData("Ctrl+Alt")]
    public void Rejects_invalid(string? text) => Assert.False(HotkeyParser.TryParse(text, out _));
}

public class SettingsStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "nova-tests-" + Guid.NewGuid());
    string File_ => Path.Combine(_dir, "settings.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Missing_file_returns_defaults_without_warning()
    {
        var r = new SettingsStore(File_).Load();
        Assert.Null(r.Warning);
        Assert.Equal(new NovaSettings().Hotkey, r.Settings.Hotkey);
    }

    [Fact]
    public void Round_trips_settings()
    {
        var store = new SettingsStore(File_);
        var s = new NovaSettings { ReducedMotion = true, Theme = ThemeMode.Dark, ClockFormat = ClockFormat.H12 };
        Assert.True(store.Save(s));
        var r = store.Load().Settings;
        Assert.True(r.ReducedMotion);
        Assert.Equal(ThemeMode.Dark, r.Theme);
        Assert.Equal(ClockFormat.H12, r.ClockFormat);
    }

    [Fact]
    public void Corrupt_file_recovers_and_is_preserved()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "{ not json");
        var r = new SettingsStore(File_).Load();
        Assert.NotNull(r.Warning);
        Assert.True(File.Exists(File_ + ".bad"));
    }

    [Fact]
    public void Reset_restores_defaults()
    {
        var store = new SettingsStore(File_);
        store.Save(new NovaSettings { DockIconSize = 80 });
        store.Reset();
        Assert.Equal(new NovaSettings().DockIconSize, store.Load().Settings.DockIconSize);
    }
}

public class LaunchValidatorTests
{
    [Theory]
    [InlineData("")] [InlineData("notepad.exe")] [InlineData(@"C:\x\evil.ps1")] [InlineData(@"C:\definitely\missing.exe")]
    public void Rejects_bad_targets(string path)
    {
        Assert.False(LaunchValidator.TryValidate(path, out var err));
        Assert.NotNull(err);
    }
}
