namespace Nova.Core.Dock;

/// <summary>Keeps the dock inside the screen: on a narrow monitor the icons shrink instead of running off the edge.</summary>
public static class DockLayout
{
    public const double MinIconSize = 20;
    public const double TileChrome = 8;     // button padding left + right
    public const double PanelChrome = 14;   // panel padding + border

    public static double WidthFor(double icon, int tiles, double spacing)
        => PanelChrome + tiles * (icon + TileChrome + spacing);

    public static double FitIconSize(double requested, int tiles, double spacing, double availableWidth)
    {
        if (double.IsNaN(requested) || requested < MinIconSize) requested = MinIconSize;
        if (tiles <= 0 || double.IsNaN(availableWidth) || availableWidth <= 0) return requested;
        if (WidthFor(requested, tiles, spacing) <= availableWidth) return requested;
        var fit = Math.Floor((availableWidth - PanelChrome) / tiles - TileChrome - spacing);
        return Math.Max(MinIconSize, Math.Min(requested, fit));
    }
}
