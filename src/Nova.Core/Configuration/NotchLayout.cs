namespace Nova.Core.Configuration;

/// <summary>Sizes shared by the top bar and the notch so they always line up.</summary>
public static class NotchLayout
{
    public const double MinBarHeight = 30;
    public const double MaxBarHeight = 64;

    /// <summary>Height of the top bar (and of the compact notch) in device-independent pixels.</summary>
    public static double BarHeightDip(double clockFontSize)
    {
        if (double.IsNaN(clockFontSize) || double.IsInfinity(clockFontSize)) clockFontSize = 18;
        return Math.Min(MaxBarHeight, Math.Max(MinBarHeight, Math.Round(clockFontSize * 1.5 + 4)));
    }
}
