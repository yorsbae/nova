using System.Windows.Media;

namespace Nova.App.Themes;

/// <summary>
/// One glass material shared by the dock and the Island, so the two read as the same family:
/// dark translucent fill, thin light edge, the same corner radius.
/// </summary>
public static class GlassLook
{
    /// <summary>Corner radius of the dock panel and the compact Island.</summary>
    public const double Radius = 22;

    /// <summary>Dark fill. <paramref name="alpha"/> 0–255 (the dock uses the lower end, the open Island the higher).</summary>
    public static Brush Fill(byte alpha)
    {
        var b = new LinearGradientBrush(Color.FromArgb(alpha, 0x1B, 0x20, 0x2C), Color.FromArgb(alpha, 0x0E, 0x12, 0x1B), 90);
        b.Freeze();
        return b;
    }

    /// <summary>The thin light edge of the glass.</summary>
    public static Brush Edge()
    {
        var b = new SolidColorBrush(Color.FromArgb(0x4A, 0xFF, 0xFF, 0xFF));
        b.Freeze();
        return b;
    }
}
