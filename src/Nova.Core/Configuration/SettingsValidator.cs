using Nova.Core.Reminders;
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

        // v3 -> v4: NOVA is meant to replace the taskbar. Settings files written by earlier versions can still carry the
        // old "off" values (a saved file overrides new defaults), which left the taskbar on screen with the dock above it
        // and no top bar. Move those files to the new behaviour once; after that every choice the user makes is kept.
        if (s.SchemaVersion < 4)
        {
            s.NotchMode = true;
            s.HideTaskbar = true;
            s.ShowRunningApps = true;
            s.DockReservesSpace = true;
            s.ShowMedia = true;
            s.ShowSystemStatus = true;
        }

        // v4 -> v5: the dock now starts small and grows under the pointer. Only files that still carry the old default size are
        // moved; a size the user picked is kept.
        if (s.SchemaVersion < 5 && s.DockIconSize == 32 && s.DockSpacing == 6) s.DockIconSize = d.DockIconSize;

        // v5 -> v6: the Island floats above the windows (no strip kept free for it) and a little further from the edge, like the concept.
        // Only the old defaults are moved; a distance the user picked is kept.
        if (s.SchemaVersion < 6)
        {
            s.ReserveTopSpace = false;
            if (s.IslandTopOffset == 8) s.IslandTopOffset = d.IslandTopOffset;
        }

        // v6 -> v7: the Mac-style dock (small icons close together on a frosted shelf, auto-hide, zoom). Saved files keep the old look
        // because a saved value overrides a new default, so move them once; sizes the user picked on purpose are kept.
        if (s.SchemaVersion < 7)
        {
            if (s.DockIconSize == 26 || s.DockIconSize == 32) s.DockIconSize = d.DockIconSize;
            if (s.DockSpacing == 6) s.DockSpacing = d.DockSpacing;
            if (s.DockBottomOffset == 6) s.DockBottomOffset = d.DockBottomOffset;
            s.DockVisibility = DockVisibility.RevealOnEdgeHover;
            s.DockMagnify = true;
            s.DockGlass = true;
            if (s.DockMagnifyScale < 1.95) s.DockMagnifyScale = d.DockMagnifyScale;
        }

        s.SchemaVersion = d.SchemaVersion;
        s.Opacity = Clamp(s.Opacity, 0.7, 1.0, d.Opacity);
        s.ClockFontSize = Clamp(s.ClockFontSize, 12, 72, d.ClockFontSize);
        s.IslandTopOffset = Clamp(s.IslandTopOffset, 0, 4000, d.IslandTopOffset);
        s.IslandOpacity = Clamp(s.IslandOpacity, 0.2, 1.0, d.IslandOpacity);
        s.DockOpacity = Clamp(s.DockOpacity, 0.2, 1.0, d.DockOpacity);
        s.IslandOffsetX = Clamp(s.IslandOffsetX, -4000, 4000, d.IslandOffsetX);
        s.DockBottomOffset = Clamp(s.DockBottomOffset, 0, 400, d.DockBottomOffset);
        s.DockIconSize = Clamp(s.DockIconSize, 24, 96, d.DockIconSize);
        s.DockSpacing = Clamp(s.DockSpacing, 0, 32, d.DockSpacing);
        s.WindowCornerRadius = Clamp(s.WindowCornerRadius, 6, 40, d.WindowCornerRadius);
        s.DockMagnifyScale = Clamp(s.DockMagnifyScale, 1.0, 2.2, d.DockMagnifyScale);
        s.IslandFaceClockSeconds = Math.Clamp(s.IslandFaceClockSeconds, 5, 300);
        s.IslandFaceShowSeconds = Math.Clamp(s.IslandFaceShowSeconds, 3, 120);
        s.HydrationIntervalMinutes = Math.Clamp(s.HydrationIntervalMinutes, 15, 240);
        s.HydrationGoalGlasses = Math.Clamp(s.HydrationGoalGlasses, 1, 20);
        s.HydrationFromHour = Math.Clamp(s.HydrationFromHour, 0, 23);
        s.HydrationToHour = Math.Clamp(s.HydrationToHour, 0, 24);
        s.PrayerLatitude = Clamp(s.PrayerLatitude, -65, 65, d.PrayerLatitude);
        s.PrayerLongitude = Clamp(s.PrayerLongitude, -180, 180, d.PrayerLongitude);
        s.PrayerCity = CleanText(s.PrayerCity, 40);
        if (s.PrayerCity.Length == 0 || (s.PrayerCity != PrayerPlaces.Custom && PrayerPlaces.Find(s.PrayerCity) is null)) s.PrayerCity = d.PrayerCity;
        s.PrayerSoundPath = CleanText(s.PrayerSoundPath, 260);
        s.AlarmSoundPath = CleanText(s.AlarmSoundPath, 260);
        if (!Enum.IsDefined(s.IslandFaceMode)) s.IslandFaceMode = d.IslandFaceMode;
        if (!Enum.IsDefined(s.IslandFaceStyle)) s.IslandFaceStyle = d.IslandFaceStyle;
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
            p.IslandTopOffset = Clamp(p.IslandTopOffset, 0, 4000, d.IslandTopOffset);
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
