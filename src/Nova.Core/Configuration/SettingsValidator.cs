using System.Globalization;

namespace Nova.Core.Configuration;

public static class SettingsValidator
{
    public const int MaxDockItems = 24;
    public const int MaxProfiles = 10;
    public const int MaxProfileNameLength = 40;
    public const string DefaultAccent = "#4C8DFF";

    /// <summary>Clamps values into safe/readable ranges and repairs invalid data in place.</summary>
    public static NovaSettings Normalize(NovaSettings? s)
    {
        s ??= new NovaSettings();
        var d = new NovaSettings();

        // v2 -> v3: the dock got smaller. Only files that still carry the old defaults are moved to the new ones;
        // anything the user changed on purpose is left alone.
        if (s.SchemaVersion < 3 && s.DockIconSize == 40 && s.DockSpacing == 8 && s.DockBottomOffset == 12)
        { s.DockIconSize = d.DockIconSize; s.DockSpacing = d.DockSpacing; s.DockBottomOffset = d.DockBottomOffset; }

        s.SchemaVersion = d.SchemaVersion;
        s.Opacity = Clamp(s.Opacity, 0.7, 1.0, d.Opacity);
        s.ClockFontSize = Clamp(s.ClockFontSize, 12, 72, d.ClockFontSize);
        s.IslandTopOffset = Clamp(s.IslandTopOffset, 0, 400, d.IslandTopOffset);
        s.DockBottomOffset = Clamp(s.DockBottomOffset, 0, 400, d.DockBottomOffset);
        s.DockIconSize = Clamp(s.DockIconSize, 24, 96, d.DockIconSize);
        s.DockSpacing = Clamp(s.DockSpacing, 0, 32, d.DockSpacing);
        s.ClipboardMaxItems = Math.Min(50, Math.Max(5, s.ClipboardMaxItems));

        if (!Enum.IsDefined(s.Theme)) s.Theme = d.Theme;
        if (!Enum.IsDefined(s.ClockFormat)) s.ClockFormat = d.ClockFormat;
        if (!Enum.IsDefined(s.IslandBehavior)) s.IslandBehavior = d.IslandBehavior;
        if (!Enum.IsDefined(s.DockVisibility)) s.DockVisibility = d.DockVisibility;
        if (!Enum.IsDefined(s.Preset)) s.Preset = d.Preset;

        if (!IsHexColor(s.AccentColor)) s.AccentColor = DefaultAccent;
        if (!HotkeyParser.TryParse(s.Hotkey, out _)) s.Hotkey = d.Hotkey;

        s.IslandMonitor = CleanText(s.IslandMonitor, 64);
        s.DockMonitor = CleanText(s.DockMonitor, 64);
        s.WeatherCity = CleanText(s.WeatherCity, 80);
        s.WeatherPlace = CleanText(s.WeatherPlace, 120);
        if (!(s.WeatherLatitude is >= -90 and <= 90) || !(s.WeatherLongitude is >= -180 and <= 180))
        { s.WeatherLatitude = double.NaN; s.WeatherLongitude = double.NaN; }   // NaN also covers NaN input
        if (s.WeatherCity.Length == 0) { s.WeatherPlace = ""; s.WeatherLatitude = double.NaN; s.WeatherLongitude = double.NaN; }

        s.DockItems = (s.DockItems ?? new())
            .Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Path))
            .Select(i => { i.Name = string.IsNullOrWhiteSpace(i.Name) ? System.IO.Path.GetFileNameWithoutExtension(i.Path) : i.Name.Trim(); return i; })
            .Take(MaxDockItems)
            .ToList();

        s.Profiles = NormalizeProfiles(s.Profiles);
        return s;
    }

    static List<LayoutProfile> NormalizeProfiles(List<LayoutProfile>? profiles)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LayoutProfile>();
        foreach (var p in profiles ?? new())
        {
            if (p is null) continue;
            var name = LayoutProfiles.CleanName(p.Name);
            if (name is null || !seen.Add(name)) continue;
            var d = new LayoutProfile();
            p.Name = name;
            p.Opacity = Clamp(p.Opacity, 0.7, 1.0, d.Opacity);
            p.ClockFontSize = Clamp(p.ClockFontSize, 12, 72, d.ClockFontSize);
            p.IslandTopOffset = Clamp(p.IslandTopOffset, 0, 400, d.IslandTopOffset);
            p.DockBottomOffset = Clamp(p.DockBottomOffset, 0, 400, d.DockBottomOffset);
            p.DockIconSize = Clamp(p.DockIconSize, 24, 96, d.DockIconSize);
            p.DockSpacing = Clamp(p.DockSpacing, 0, 32, d.DockSpacing);
            if (!Enum.IsDefined(p.Theme)) p.Theme = d.Theme;
            if (!Enum.IsDefined(p.ClockFormat)) p.ClockFormat = d.ClockFormat;
            if (!Enum.IsDefined(p.IslandBehavior)) p.IslandBehavior = d.IslandBehavior;
            if (!Enum.IsDefined(p.DockVisibility)) p.DockVisibility = d.DockVisibility;
            if (!IsHexColor(p.AccentColor)) p.AccentColor = DefaultAccent;
            p.IslandMonitor = CleanText(p.IslandMonitor, 64);
            p.DockMonitor = CleanText(p.DockMonitor, 64);
            result.Add(p);
            if (result.Count == MaxProfiles) break;
        }
        return result;
    }

    static string CleanText(string? t, int max)
    {
        if (string.IsNullOrWhiteSpace(t)) return "";
        var clean = new string(t.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > max ? clean[..max] : clean;
    }

    static double Clamp(double v, double min, double max, double fallback)
        => double.IsNaN(v) || double.IsInfinity(v) ? fallback : Math.Min(max, Math.Max(min, v));

    static bool IsHexColor(string? c)
        => c is { Length: 7 } && c[0] == '#' &&
           int.TryParse(c.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
}
