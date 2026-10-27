using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Nova.Core.Configuration;
using Path = System.Windows.Shapes.Path;   // System.IO is an implicit using; be explicit so "Path" is never ambiguous

namespace Nova.App.Features.DynamicIsland;

/// <summary>
/// Anime eyes drawn like the reference sheet: a pointed almond lid (not a round ball) with a thick upper lash line that flicks out
/// at the outer corner, lashes, a tall gradient iris that the lid cuts off at the top, shadow under the lid, highlights, and an
/// optional glowing aura. The scary variants (Yandere, Demon, Hollow) tilt the lid, add eye bags, and animate pupil and aura.
/// Everything is plain WPF shapes (no images), built once per face and animated only while the face is shown.
/// </summary>
public partial class IslandWindow
{
    sealed record EyeLook(
        string Iris1, string Iris2,
        string Sclera = "#FFF8F8FF", string Lid = "#FF1B1226", string Pupil = "#FF0B0B12",
        string? Aura = null,
        double Slant = 0, double IrisW = 0.52, double PupilSize = 0.40,
        bool Slit = false, bool Wing = true, bool Lashes = true, bool Bags = false, bool Sparkle = false, bool Highlights = true);

    // ---- the catalogue: one look per style ----
    static readonly EyeLook LookAnimeA = new("#FF9BDCFF", "#FF2457D6");                                              // calm blue
    static readonly EyeLook LookAnimeB = new("#FFE3B8FF", "#FF6A28C4", Lid: "#FF140A22", IrisW: 0.50);                // violet, long lashes
    static readonly EyeLook LookAnimeC = new("#FFC2FFDC", "#FF1C9A5E", Sparkle: true, IrisW: 0.56);                   // green, star sparkle
    static readonly EyeLook LookAnimeAura = new("#FFE0FFFF", "#FF1AA3D9", Aura: "#FF4FE8FF", Sparkle: true, IrisW: 0.54);   // glowing cyan
    static readonly EyeLook LookYandere = new("#FFFF6B8A", "#FF6E0016", Sclera: "#FFF4E6EC", Lid: "#FF2A0008", Pupil: "#FF1A0005",
        Aura: "#FFFF1744", Slant: 5, IrisW: 0.60, PupilSize: 0.20, Bags: true, Highlights: false);                      // tiny pupil, no shine, red aura
    static readonly EyeLook LookDemon = new("#FFFFE27A", "#FFC4500C", Sclera: "#FF140808", Lid: "#FFB8400F", Pupil: "#FF050000",
        Aura: "#FFFF7A1A", Slant: 15, IrisW: 0.58, Slit: true, Bags: true, Highlights: false);                          // black eye, gold slit, angry
    static readonly EyeLook LookHollow = new("#FFFFFFFF", "#FFA9B8FF", Sclera: "#FF050508", Lid: "#FF8F9AD0", Pupil: "#FF000000",
        Aura: "#FFB9C6FF", Slant: 8, IrisW: 0.50, PupilSize: 0, Bags: true, Highlights: false);                         // black eye, glowing white iris

    static string EyeNum(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string EyePt(double x, double y) => EyeNum(x) + "," + EyeNum(y);
    static Color EyeColor(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
    static Color EyeAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    static Color EyeDarken(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    static Point EyeBezier(Point a, Point b, Point c, Point d, double t)
    {
        double u = 1 - t;
        return new Point(
            u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X,
            u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y);
    }

    Path EyePath(string data, Brush? fill, Brush? stroke, double thickness, bool round = true)
    {
        var p = new Path { Data = Geometry.Parse(data), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
        if (round) { p.StrokeStartLineCap = PenLineCap.Round; p.StrokeEndLineCap = PenLineCap.Round; p.StrokeLineJoin = PenLineJoin.Round; }
        return p;
    }

    static Ellipse EyeBall(double w, double h, double left, double top, Brush? fill)
        => new() { Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(left, top, 0, 0), Fill = fill };

    /// <summary>One almond-shaped anime eye. <paramref name="idx"/> 0 is the left eye (outer corner on the left); 1 is the mirrored right eye.</summary>
    Grid AlmondEye(EyeLook k, double h, int idx)
    {
        double w = h * 1.05;
        var g = new Grid { Width = w, Height = h, RenderTransformOrigin = new Point(0.5, 0.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(h * 0.04, 0, h * 0.04, 0) };

        // lid outline: outer corner on the left (a little up), inner corner on the right (a little down)
        var c0 = new Point(0.02 * w, 0.50 * h); var c1 = new Point(0.20 * w, 0.00);
        var c2 = new Point(0.72 * w, -0.02 * h); var c3 = new Point(0.98 * w, 0.66 * h);
        string upper = $"M {EyePt(c0.X, c0.Y)} C {EyePt(c1.X, c1.Y)} {EyePt(c2.X, c2.Y)} {EyePt(c3.X, c3.Y)}";
        string lowerOnly = $"M {EyePt(c3.X, c3.Y)} C {EyePt(0.78 * w, h)} {EyePt(0.30 * w, h)} {EyePt(c0.X, c0.Y)}";
        string almond = upper + $" C {EyePt(0.78 * w, h)} {EyePt(0.30 * w, h)} {EyePt(c0.X, c0.Y)} Z";

        var lidColor = EyeColor(k.Lid);
        var iris1 = EyeColor(k.Iris1); var iris2 = EyeColor(k.Iris2);
        var sclera = EyeColor(k.Sclera);

        // aura: a soft glow behind the eye, wider than the lid (may spill a little past it)
        if (k.Aura is not null)
        {
            var a = EyeColor(k.Aura);
            var aura = new Ellipse
            {
                Width = w * 1.40, Height = h * 1.35, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
                Fill = new RadialGradientBrush { GradientStops = { new GradientStop(EyeAlpha(a, 0xB4), 0), new GradientStop(EyeAlpha(a, 0x55), 0.5), new GradientStop(EyeAlpha(a, 0x00), 1) } }
            };
            g.Children.Add(aura);
            bool scary = k.Slant >= 4;
            Pulse(aura, scary ? 0.25 : 0.45, 1.0, scary ? 0.7 : 1.3);
        }

        // everything that is mirrored / tilted lives in "art"; the blink (set later by BuildFace) squashes the whole eye
        var art = new Grid { Width = w, Height = h, RenderTransformOrigin = new Point(0.5, 0.5) };
        var tg = new TransformGroup();
        tg.Children.Add(new RotateTransform(k.Slant));
        tg.Children.Add(new ScaleTransform(idx == 1 ? -1 : 1, 1));
        art.RenderTransform = tg;
        g.Children.Add(art);

        // eye bags: a dark crescent under the lid for the creepy looks
        if (k.Bags)
        {
            var bag = EyePath(lowerOnly, null, new SolidColorBrush(EyeAlpha(lidColor, 0x66)), Math.Max(1.5, h * 0.09));
            bag.RenderTransform = new TranslateTransform(0, h * 0.07);
            art.Children.Add(bag);
        }

        // inside the lid (clipped to the almond): white, iris, pupil, shine, shadow of the lid
        var inner = new Grid { Width = w, Height = h, Clip = Geometry.Parse(almond), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        inner.Children.Add(EyePath(almond, new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops = { new GradientStop(EyeDarken(sclera, 0.78), 0), new GradientStop(sclera, 0.45), new GradientStop(sclera, 1) } }, null, 0, round: false));

        double iw = w * k.IrisW, ih = h * 1.12, cx = 0.52 * w;
        var iris = EyeBall(iw, ih, cx - iw / 2, -0.04 * h, new RadialGradientBrush
        {
            Center = new Point(0.5, 0.62), GradientOrigin = new Point(0.5, 0.80), RadiusX = 0.62, RadiusY = 0.62,
            GradientStops = { new GradientStop(iris1, 0), new GradientStop(iris2, 0.78), new GradientStop(EyeDarken(iris2, 0.55), 1) }
        });
        iris.Stroke = new SolidColorBrush(EyeDarken(iris2, 0.45)); iris.StrokeThickness = Math.Max(1, h * 0.035);
        inner.Children.Add(iris);

        Ellipse? pupil = null;
        if (k.PupilSize > 0 || k.Slit)
        {
            double pw = k.Slit ? iw * 0.14 : iw * k.PupilSize, ph = k.Slit ? h * 0.98 : ih * Math.Max(0.28, k.PupilSize * 1.2);
            pupil = EyeBall(pw, ph, cx - pw / 2, 0.52 * h - ph / 2, Solid(k.Pupil));
            inner.Children.Add(pupil);
        }

        // the lash line casts a soft shadow onto the top of the eye (under the shine, so the shine stays white)
        inner.Children.Add(new Rectangle
        {
            Width = w, Height = h * 0.42, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Fill = new LinearGradientBrush(Color.FromArgb(0xB4, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90)
        });

        Ellipse? shineSmall = null;
        if (k.Highlights)
        {
            inner.Children.Add(EyeBall(h * 0.21, h * 0.21, cx - iw * 0.30, 0.16 * h, Brushes.White));
            shineSmall = EyeBall(h * 0.09, h * 0.09, cx + iw * 0.12, 0.60 * h, Brushes.White);
            shineSmall.Opacity = 0.85;
            inner.Children.Add(shineSmall);
        }
        if (k.Sparkle)
        {
            var star = new TextBlock { Text = "✦", FontSize = h * 0.36, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(cx - iw * 0.38, 0.05 * h, 0, 0) };
            inner.Children.Add(star);
            Pulse(star, 0.35, 1, 0.8);
        }
        art.Children.Add(inner);

        // double-eyelid crease above the lid
        var crease = EyePath(upper, null, new SolidColorBrush(EyeAlpha(lidColor, 0x70)), Math.Max(1, h * 0.03));
        crease.RenderTransform = new TranslateTransform(0, -h * 0.13);
        art.Children.Add(crease);

        // the thick upper lash line, with the flick (wing) at the outer corner
        var lidBrush = new SolidColorBrush(lidColor);
        art.Children.Add(EyePath(upper, null, lidBrush, Math.Max(1.8, h * 0.11)));
        if (k.Wing)
            art.Children.Add(EyePath($"M {EyePt(0.05 * w, 0.46 * h)} Q {EyePt(-0.06 * w, 0.37 * h)} {EyePt(-0.17 * w, 0.21 * h)} Q {EyePt(0.04 * w, 0.26 * h)} {EyePt(0.26 * w, 0.12 * h)} Z", lidBrush, null, 0, round: false));

        // lashes: short strokes leaning towards the outer corner
        if (k.Lashes)
        {
            foreach (var (t, len) in new[] { (0.10, 0.17), (0.24, 0.20), (0.38, 0.19), (0.52, 0.15) })
            {
                var a = EyeBezier(c0, c1, c2, c3, t); var b = EyeBezier(c0, c1, c2, c3, t + 0.02);
                double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Max(0.001, Math.Sqrt(dx * dx + dy * dy));
                double ox = dy / l - 0.55, oy = -dx / l;                    // upward normal, turned towards the outer corner
                double ol = Math.Max(0.001, Math.Sqrt(ox * ox + oy * oy)); ox /= ol; oy /= ol;
                art.Children.Add(EyePath($"M {EyePt(a.X, a.Y)} L {EyePt(a.X + ox * h * len, a.Y + oy * h * len)}", null, lidBrush, Math.Max(1.1, h * 0.045)));
            }
        }

        // ---- extra life per look (on top of the shared blink / look-around / mood animations) ----
        if (shineSmall is not null) Pulse(shineSmall, 0.4, 1, 1.5);
        if (pupil is not null && k.Slit)
        {
            var sc = new ScaleTransform(1, 1); pupil.RenderTransformOrigin = new Point(0.5, 0.5); pupil.RenderTransform = sc;
            _shapeStart += () => sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 3.0, TimeSpan.FromSeconds(1.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            _shapeStop += () => { sc.BeginAnimation(ScaleTransform.ScaleXProperty, null); sc.ScaleX = 1; };
        }
        else if (pupil is not null && k.Aura is not null && k.Slant > 0) Breathe(pupil, 0.5, 0.5);   // Yandere: the pupil keeps shrinking and widening
        if (k.PupilSize <= 0 && !k.Slit)                                                              // Hollow: the white iris glitches now and then
        {
            _shapeStart += () =>
            {
                var f = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3.1), RepeatBehavior = RepeatBehavior.Forever };
                foreach (var (v, t) in new[] { (1.0, 0.0), (1.0, 1.9), (0.2, 1.95), (1.0, 2.02), (0.5, 2.1), (1.0, 2.16), (1.0, 3.1) })
                    f.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, TimeSpan.FromSeconds(t)));
                iris.BeginAnimation(UIElement.OpacityProperty, f);
            };
            _shapeStop += () => { iris.BeginAnimation(UIElement.OpacityProperty, null); iris.Opacity = 1; };
        }
        return g;
    }
}
