namespace Nova.Core.Dock;

/// <summary>
/// The macOS-style zoom of the dock: the icon under the pointer is the biggest and its neighbours follow a smooth bell curve.
/// Pure math, so it can be tested; the dock window only feeds it positions.
/// </summary>
public static class DockMagnify
{
    public const double MaxAllowedScale = 2.2;

    /// <summary>Scale (1 = resting size) of an icon whose centre is <paramref name="centerX"/> when the pointer is at <paramref name="pointerX"/>.</summary>
    public static double Scale(double pointerX, double centerX, double sigma, double maxScale)
    {
        if (double.IsNaN(pointerX) || double.IsNaN(centerX) || double.IsNaN(sigma) || double.IsNaN(maxScale)) return 1;
        if (double.IsInfinity(pointerX) || double.IsInfinity(centerX)) return 1;
        if (sigma <= 0 || maxScale <= 1) return 1;
        maxScale = Math.Min(maxScale, MaxAllowedScale);
        double d = (pointerX - centerX) / sigma;
        return 1 + (maxScale - 1) * Math.Exp(-d * d);
    }

    /// <summary>How far (in DIPs) the zoom reaches to each side: about one and a bit icon widths.</summary>
    public static double Sigma(double icon, double spacing)
        => Math.Max(8, (icon + DockLayout.TileChrome + spacing) * 1.3);

    /// <summary>Transparent room the window needs above the resting dock so the biggest icon is never cut off.</summary>
    public static double HeadroomFor(double icon, double maxScale)
    {
        if (double.IsNaN(maxScale) || maxScale < 1) maxScale = 1;
        maxScale = Math.Min(maxScale, MaxAllowedScale);
        return Math.Ceiling(icon * (maxScale - 1)) + 12;
    }

    /// <summary>One animation step towards the target (exponential ease). Snaps when it is close enough, so animation can stop.</summary>
    public static double Approach(double current, double target, double rate = 0.28)
    {
        if (double.IsNaN(current)) return target;
        double next = current + (target - current) * Math.Min(1, Math.Max(0.01, rate));
        return Math.Abs(next - target) < 0.004 ? target : next;
    }
}
