using Nova.Core.Configuration;
using Xunit;

namespace Nova.Core.Tests;

public class NotchTests
{
    [Fact]
    public void Defaults_replace_taskbar_but_never_change_the_windows_theme_without_consent()
    {
        var s = new NovaSettings();
        Assert.True(s.NotchMode);
        Assert.True(s.HideTaskbar);         // the dock replaces the taskbar (restored on exit)
        Assert.True(s.ShowRunningApps);
        Assert.False(s.WindowsDarkMode);    // changes the Windows theme: opt-in only
    }

    [Theory]
    [InlineData(18, 31)]
    [InlineData(12, 30)]     // never smaller than the minimum
    [InlineData(72, 64)]     // never taller than the maximum
    [InlineData(double.NaN, 31)]
    [InlineData(double.PositiveInfinity, 31)]
    public void Bar_height_is_clamped(double fontSize, double expected)
        => Assert.Equal(expected, NotchLayout.BarHeightDip(fontSize));

    [Fact]
    public void Notch_settings_round_trip_through_the_store()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nova-notch-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.Save(new NovaSettings { NotchMode = false, HideTaskbar = false, ShowRunningApps = false, WindowsDarkMode = true });
            var loaded = store.Load().Settings;
            Assert.False(loaded.NotchMode);
            Assert.False(loaded.HideTaskbar);
            Assert.False(loaded.ShowRunningApps);
            Assert.True(loaded.WindowsDarkMode);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void Old_settings_files_without_the_new_fields_get_notch_mode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nova-old-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ \"SchemaVersion\": 2, \"ClockFontSize\": 20 }");
            var loaded = new SettingsStore(path).Load().Settings;
            Assert.True(loaded.NotchMode);
            Assert.True(loaded.HideTaskbar);
            Assert.Equal(20, loaded.ClockFontSize);
        }
        finally { File.Delete(path); }
    }
}
