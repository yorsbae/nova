namespace Nova.Core.Status;

/// <summary>Maps status values to Segoe MDL2 Assets / Segoe Fluent Icons code points for the top bar.</summary>
public static class StatusGlyphs
{
    public const string Wifi = "\uE701";
    public const string Ethernet = "\uE839";
    public const string Bluetooth = "\uE702";
    public const string Mute = "\uE74F";
    public const string Charging = "\uE83E";

    public static string Volume(double level, bool muted)
    {
        if (muted) return Mute;
        if (double.IsNaN(level) || level <= 0.001) return "\uE992";
        return level < 0.34 ? "\uE993" : level < 0.67 ? "\uE994" : "\uE995";
    }

    public static string Battery(int percent, bool charging)
    {
        if (charging) return Charging;
        percent = Math.Min(100, Math.Max(0, percent));
        if (percent >= 95) return "\uE83F";
        return ((char)(0xE850 + percent / 10)).ToString();
    }

    /// <summary>Wi-Fi strength shown as opacity of one glyph (negative = unknown, shown at full strength).</summary>
    public static double WifiOpacity(int signalPercent)
        => signalPercent < 0 ? 1.0 : signalPercent >= 70 ? 1.0 : signalPercent >= 45 ? 0.75 : signalPercent >= 20 ? 0.55 : 0.4;
}
