namespace Nova.Core.Configuration;

public enum ThemeMode { System, Light, Dark }
public enum ClockFormat { H24, H12 }
public enum IslandBehavior { AlwaysCompact, HiddenUntilInvoked, Adaptive }
public enum DockVisibility { AlwaysVisible, RevealOnEdgeHover }
public enum LayoutPreset { Minimal, Balanced, Custom }

public sealed class DockItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>A named, switchable look-and-layout. Never contains hotkey, dock shortcuts or accessibility choices.</summary>
public sealed class LayoutProfile
{
    public string Name { get; set; } = "";
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public string AccentColor { get; set; } = "#4C8DFF";
    public double Opacity { get; set; } = 0.95;
    public ClockFormat ClockFormat { get; set; } = ClockFormat.H24;
    public double ClockFontSize { get; set; } = 18;
    public IslandBehavior IslandBehavior { get; set; } = IslandBehavior.AlwaysCompact;
    public double IslandTopOffset { get; set; } = 8;
    public DockVisibility DockVisibility { get; set; } = DockVisibility.AlwaysVisible;
    public double DockBottomOffset { get; set; } = 6;
    public double DockIconSize { get; set; } = 32;
    public double DockSpacing { get; set; } = 6;
    public string IslandMonitor { get; set; } = "";
    public string DockMonitor { get; set; } = "";
}

/// <summary>User settings. Every default here is the "known-good layout" used by Reset.</summary>
public sealed class NovaSettings
{
    public int SchemaVersion { get; set; } = 4;
    public LayoutPreset Preset { get; set; } = LayoutPreset.Balanced;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public string AccentColor { get; set; } = "#4C8DFF";
    public double Opacity { get; set; } = 0.95;
    /// <summary>Use an opaque rectangular window instead of a transparent one (for GPUs/drivers that mishandle transparency). Needs restart.</summary>
    public bool OpaqueFallback { get; set; } = false;

    public ClockFormat ClockFormat { get; set; } = ClockFormat.H24;
    public double ClockFontSize { get; set; } = 18;

    public IslandBehavior IslandBehavior { get; set; } = IslandBehavior.AlwaysCompact;
    public double IslandTopOffset { get; set; } = 8;
    public bool TemporaryNotices { get; set; } = false;
    /// <summary>Windows device name such as \\.\DISPLAY2. Empty = primary monitor.</summary>
    public string IslandMonitor { get; set; } = "";

    public DockVisibility DockVisibility { get; set; } = DockVisibility.AlwaysVisible;
    public double DockBottomOffset { get; set; } = 6;
    public double DockIconSize { get; set; } = 32;
    public double DockSpacing { get; set; } = 6;
    public string DockMonitor { get; set; } = "";
    /// <summary>Reserves the strip the dock occupies (like the taskbar does), so maximized windows such as the browser stop above it instead of going under it.</summary>
    public bool DockReservesSpace { get; set; } = true;
    /// <summary>Glass look: the dock strip is see-through so only the Start button and app icons float over the wallpaper.</summary>
    public bool DockGlass { get; set; } = true;
    public List<DockItem> DockItems { get; set; } = new();

    // --- Phase 2 integrations ---
    public bool ShowMedia { get; set; } = true;
    public bool ShowSystemStatus { get; set; } = true;

    /// <summary>Opt-in. Sends the city name (once) and its coordinates (periodically) to open-meteo.com.</summary>
    public bool WeatherEnabled { get; set; } = false;
    public string WeatherCity { get; set; } = "";
    public string WeatherPlace { get; set; } = "";      // resolved display name
    public double WeatherLatitude { get; set; } = double.NaN;
    public double WeatherLongitude { get; set; } = double.NaN;
    public bool WeatherFahrenheit { get; set; } = false;

    /// <summary>Opt-in. Text only, kept in memory only, never written to disk.</summary>
    public bool ClipboardHistoryEnabled { get; set; } = false;
    public int ClipboardMaxItems { get; set; } = 20;

    // --- Phase 3 ---
    public List<LayoutProfile> Profiles { get; set; } = new();

    // --- Mac-style notch mode ---
    /// <summary>Docks a bar to the top edge of the screen (Windows then keeps windows below it) and attaches the Island to it as a notch.</summary>
    public bool NotchMode { get; set; } = true;
    /// <summary>Replaces the Windows taskbar and Start button with the dock (taskbar hidden while NOVA runs, restored on exit).</summary>
    public bool HideTaskbar { get; set; } = true;
    /// <summary>Shows the apps that are currently open in the dock (with a dot), like the macOS Dock.</summary>
    public bool ShowRunningApps { get; set; } = true;
    /// <summary>Opt-in. Switches Windows to dark mode while NOVA runs (restored on exit).</summary>
    public bool WindowsDarkMode { get; set; } = false;

    // --- v2.2: Island like NotchNook / Alcove ---
    /// <summary>The notch grows when the pointer rests on it and shrinks when it leaves (never takes keyboard focus by itself).</summary>
    public bool IslandHoverExpand { get; set; } = true;
    /// <summary>Temporary file shelf: drop files on the notch, drag them out again. Paths only, memory only.</summary>
    public bool ShelfEnabled { get; set; } = true;
    /// <summary>Opt-in. Sends the artist and track title to lrclib.net to show synced lyrics.</summary>
    public bool LyricsEnabled { get; set; } = false;

    /// <summary>Live activities on the Island: volume changes, charger plugged in, low battery, Bluetooth / Wi-Fi connecting.</summary>
    public bool ActivityAlerts { get; set; } = true;
    /// <summary>Keeps NOVA light: lower CPU priority, Windows efficiency mode (EcoQoS) and trimming of unused memory after it has been idle.</summary>
    public bool EcoMode { get; set; } = true;

    public bool ReducedMotion { get; set; } = false;
    public bool HighContrastOverride { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public string Hotkey { get; set; } = "Ctrl+Alt+N";

    /// <summary>Deep copy, so a settings dialog can edit without touching live state.</summary>
    public NovaSettings Clone()
    {
        var c = (NovaSettings)MemberwiseClone();
        c.DockItems = DockItems.Select(i => new DockItem { Name = i.Name, Path = i.Path }).ToList();
        c.Profiles = Profiles.Select(p => (LayoutProfile)p.Clone()).ToList();
        return c;
    }
}

public static class LayoutProfileExtensions
{
    public static LayoutProfile Clone(this LayoutProfile p) => new()
    {
        Name = p.Name, Theme = p.Theme, AccentColor = p.AccentColor, Opacity = p.Opacity, ClockFormat = p.ClockFormat,
        ClockFontSize = p.ClockFontSize, IslandBehavior = p.IslandBehavior, IslandTopOffset = p.IslandTopOffset,
        DockVisibility = p.DockVisibility, DockBottomOffset = p.DockBottomOffset, DockIconSize = p.DockIconSize,
        DockSpacing = p.DockSpacing, IslandMonitor = p.IslandMonitor, DockMonitor = p.DockMonitor
    };
}
