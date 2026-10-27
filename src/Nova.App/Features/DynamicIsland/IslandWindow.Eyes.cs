using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Nova.Core.Configuration;

namespace Nova.App.Features.DynamicIsland;

/// <summary>
/// The eye catalogue of the compact Island face. Each style is drawn with plain shapes (no images) and has its own extra animation on top of
/// the shared blinking and looking around: spinning tomoe, rippling rings, flickering ghoul eye, a slit pupil that widens, and lightning
/// (the "Zone" and "Shadow Monarch" eyes). The animations are collected in <c>_shapeStart</c> / <c>_shapeStop</c> and only run while the face is shown.
/// </summary>
public partial class IslandWindow
{
    Action? _shapeStart, _shapeStop;

    Ellipse Ring(double d, string color, double thickness, double opacity = 1)
        => new() { Width = d, Height = d, Stroke = Solid(color), StrokeThickness = thickness, Opacity = opacity };

    Border Dot(double d, string color) => Blob(d, d, d / 2, null, Solid(color));

    /// <summary>A zig-zag lightning bolt, drawn in a box of about 0.8·h, placed with a margin and turned by an angle.</summary>
    Path Bolt(double h, string color, double x, double y, double angle, double scale = 1)
    {
        var f = new PathFigure { StartPoint = new Point(0, 0), IsClosed = false };
        var pts = new[] { (0.10, 0.18), (0.02, 0.22), (0.14, 0.46), (0.06, 0.50), (0.20, 0.80) };
        f.Segments.Add(new PolyLineSegment(pts.Select(p => new Point(p.Item1 * h * scale, p.Item2 * h * scale)), true));
        return new Path
        {
            Data = new PathGeometry(new[] { f }), Stroke = Solid(color), StrokeThickness = Math.Max(1.2, h * 0.055),
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x, y, 0, 0),
            RenderTransformOrigin = new Point(0, 0), RenderTransform = new RotateTransform(angle), Opacity = 0.9
        };
    }

    /// <summary>Flashes a bolt now and then, like crackling electricity; each bolt has its own rhythm.</summary>
    void Flash(Path bolt, double seconds, double offset)
    {
        _shapeStart += () =>
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(offset) };
            foreach (var (v, f) in new[] { (0.0, 0.0), (0.0, 0.50), (1.0, 0.52), (0.15, 0.58), (1.0, 0.62), (0.0, 0.72), (0.0, 1.0) })
                a.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, TimeSpan.FromSeconds(f * seconds)));
            bolt.BeginAnimation(UIElement.OpacityProperty, a);
        };
        _shapeStop += () => { bolt.BeginAnimation(UIElement.OpacityProperty, null); bolt.Opacity = 0.9; };
    }

    void Spin(UIElement el, double seconds, bool reverse = false)
    {
        var rot = new RotateTransform(0);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = rot;
        _shapeStart += () => rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, reverse ? -360 : 360, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever });
        _shapeStop += () => { rot.BeginAnimation(RotateTransform.AngleProperty, null); rot.Angle = 0; };
    }

    void Pulse(UIElement el, double from, double to, double seconds)
    {
        _shapeStart += () => el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        _shapeStop += () => { el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = to; };
    }

    void Breathe(UIElement el, double to, double seconds)
    {
        var sc = new ScaleTransform(1, 1);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = sc;
        _shapeStart += () =>
        {
            var a = new DoubleAnimation(1, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a); sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        };
        _shapeStop += () => { sc.BeginAnimation(ScaleTransform.ScaleXProperty, null); sc.BeginAnimation(ScaleTransform.ScaleYProperty, null); sc.ScaleX = sc.ScaleY = 1; };
    }

    /// <summary>One eye of the given style; <paramref name="idx"/> is 0 (left) or 1 (right), because some characters have two different eyes.</summary>
    FrameworkElement MakeEye(IslandFaceStyle style, double h, int idx)
    {
        // The anime eyes (A/B/C, Aura, Yandere, Demon, Hollow) are real eyes with moving lids: see RealEye.cs and IslandWindow.EyeBrain.cs
        if (RealLook(style) is { } look) return new RealEye(look, h, idx);

        var g = new Grid { Width = h * 0.92, Height = h, RenderTransformOrigin = new Point(0.5, 0.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(h * 0.04, 0, h * 0.04, 0) };
        void Ball(string sclera) => g.Children.Add(Blob(h * 0.84, h * 0.62, h * 0.31, null, Solid(sclera)));
        Border Center(double d, string color) { var b = Dot(d, color); g.Children.Add(b); return b; }

        switch (style)
        {
            case IslandFaceStyle.Sharingan:   // red iris, three tomoe circling the pupil
            {
                Ball("#FFF4F4F4");
                Center(h * 0.58, "#FFD62828");
                g.Children.Add(Ring(h * 0.40, "#FF1A0000", Math.Max(1, h * 0.03), 0.8));
                var orbit = new Grid { Width = h * 0.46, Height = h * 0.46 };
                for (int i = 0; i < 3; i++)
                {
                    var t = Dot(h * 0.11, "#FF120000");
                    t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Top;
                    t.RenderTransformOrigin = new Point(0.5, (h * 0.23) / (h * 0.11));
                    t.RenderTransform = new RotateTransform(i * 120);
                    orbit.Children.Add(t);
                }
                g.Children.Add(orbit);
                Spin(orbit, 3.2);
                Center(h * 0.13, "#FF120000");
                break;
            }
            case IslandFaceStyle.Rinnegan:   // lilac iris with rippling rings
            {
                Ball("#FFEDE8F7");
                Center(h * 0.60, "#FFB9A7EA");
                var rings = new Grid { Width = h * 0.6, Height = h * 0.6 };
                foreach (var d in new[] { 0.52, 0.38, 0.24 }) rings.Children.Add(Ring(h * d, "#FF4B2C8F", Math.Max(1, h * 0.035)));
                g.Children.Add(rings);
                Center(h * 0.08, "#FF2A1456");
                Breathe(rings, 1.14, 1.3);
                break;
            }
            case IslandFaceStyle.Ghoul:   // left: a plain pale eye; right: black sclera, glowing red iris that flickers
            {
                if (idx == 0)
                {
                    Ball("#FFEDEDED"); Center(h * 0.52, "#FFC9CED6");
                    g.Children.Add(Ring(h * 0.52, "#FF8A919C", Math.Max(1, h * 0.03)));
                    Center(h * 0.14, "#FF222222");
                }
                else
                {
                    Ball("#FF070707");
                    var glow = Center(h * 0.66, "#FFFF1E2D"); glow.Opacity = 0.28;
                    var iris = Center(h * 0.46, "#FFE5242B");
                    Center(h * 0.15, "#FF110000");
                    var spark = Center(h * 0.08, "#FFFFB3B3"); spark.HorizontalAlignment = HorizontalAlignment.Left; spark.Margin = new Thickness(h * 0.22, 0, 0, h * 0.14);
                    Pulse(glow, 0.12, 0.55, 0.9);
                    _shapeStart += () =>
                    {
                        var f = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(2.4), RepeatBehavior = RepeatBehavior.Forever };
                        foreach (var (v, t) in new[] { (1.0, 0.0), (1.0, 1.2), (0.45, 1.26), (1.0, 1.32), (0.6, 1.4), (1.0, 1.46), (1.0, 2.4) })
                            f.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, TimeSpan.FromSeconds(t)));
                        iris.BeginAnimation(UIElement.OpacityProperty, f);
                    };
                    _shapeStop += () => { iris.BeginAnimation(UIElement.OpacityProperty, null); iris.Opacity = 1; };
                }
                break;
            }
            case IslandFaceStyle.Void:   // black eye with one red ring, glowing slowly
            {
                Ball("#FF050505");
                var ring = Ring(h * 0.46, "#FFFF2A2A", Math.Max(1.5, h * 0.07));
                g.Children.Add(ring);
                var halo = Ring(h * 0.62, "#FFFF2A2A", Math.Max(1, h * 0.03), 0.4);
                g.Children.Add(halo);
                Center(h * 0.12, "#FFFF2A2A");
                Pulse(ring, 0.5, 1, 1.1); Pulse(halo, 0.1, 0.6, 1.1);
                break;
            }
            case IslandFaceStyle.Alchemy:   // left: small blue iris; right: a red transmutation circle that turns
            {
                Ball("#FFFAFAFA");
                if (idx == 0) { Center(h * 0.30, "#FF3F78C8"); Center(h * 0.12, "#FF0B1F3D"); }
                else
                {
                    var circle = new Grid { Width = h * 0.62, Height = h * 0.62 };
                    circle.Children.Add(Ring(h * 0.60, "#FFD01F1F", Math.Max(1.2, h * 0.045)));
                    circle.Children.Add(new TextBlock { Text = "✡", FontSize = h * 0.46, Foreground = Solid("#FFD01F1F"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    g.Children.Add(circle);
                    Spin(circle, 7);
                }
                break;
            }
            case IslandFaceStyle.Web:   // blue iris with a shimmering web of rings
            {
                Ball("#FFF7F7F7");
                Center(h * 0.58, "#FF3D7BFF");
                var web = new Grid { Width = h * 0.58, Height = h * 0.58 };
                foreach (var d in new[] { 0.54, 0.40, 0.26 }) web.Children.Add(Ring(h * d, "#FFBFD8FF", Math.Max(1, h * 0.025), 0.75));
                web.Children.Add(new Border { Width = h * 0.56, Height = Math.Max(1, h * 0.02), Background = Solid("#80BFD8FF") });
                web.Children.Add(new Border { Height = h * 0.56, Width = Math.Max(1, h * 0.02), Background = Solid("#80BFD8FF") });
                g.Children.Add(web);
                Center(h * 0.14, "#FF0A1A44");
                Spin(web, 9, reverse: idx == 1); Pulse(web, 0.55, 1, 1.4);
                break;
            }
            case IslandFaceStyle.Cat:   // slit pupil that widens and narrows (left pink, right amber)
            {
                Ball("#FFFAFAFA");
                Center(h * 0.58, idx == 0 ? "#FFFF4D8D" : "#FFFFA31A");
                var slit = Blob(h * 0.10, h * 0.48, h * 0.05, null, Brushes.Black);
                g.Children.Add(slit);
                var sc = new ScaleTransform(1, 1); slit.RenderTransformOrigin = new Point(0.5, 0.5); slit.RenderTransform = sc;
                _shapeStart += () => sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 3.2, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                _shapeStop += () => { sc.BeginAnimation(ScaleTransform.ScaleXProperty, null); sc.ScaleX = 1; };
                var shine = Dot(h * 0.09, "#FFFFFFFF"); shine.HorizontalAlignment = HorizontalAlignment.Right; shine.VerticalAlignment = VerticalAlignment.Top; shine.Margin = new Thickness(0, h * 0.13, h * 0.2, 0);
                g.Children.Add(shine);
                break;
            }
            case IslandFaceStyle.Galaxy:   // purple iris with a twinkling star
            {
                Ball("#FFF3EEFA");
                Center(h * 0.60, "#FF8E2DE2"); Center(h * 0.38, "#FFC77DFF");
                var star = new TextBlock { Text = "✦", FontSize = h * 0.44, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                g.Children.Add(star);
                Spin(star, 6); Pulse(star, 0.4, 1, 0.7);
                break;
            }
            case IslandFaceStyle.Zone:   // light-blue eyes crackling with lightning, like the Zone
            {
                Ball("#FFF2FAFF");
                var aura = Center(h * 0.74, "#FF59C8FF"); aura.Opacity = 0.25;
                Center(h * 0.54, "#FF7FD9FF");
                g.Children.Add(Ring(h * 0.54, "#FFD8F6FF", Math.Max(1, h * 0.03)));
                Center(h * 0.15, "#FF06324F");
                var hi = Center(h * 0.10, "#FFFFFFFF"); hi.HorizontalAlignment = HorizontalAlignment.Left; hi.VerticalAlignment = VerticalAlignment.Top; hi.Margin = new Thickness(h * 0.28, h * 0.2, 0, 0);
                var b1 = Bolt(h, "#FFB5EEFF", h * 0.62, -h * 0.24, 8);
                var b2 = Bolt(h, "#FF7FD9FF", -h * 0.06, h * 0.50, -18, 0.8);
                g.Children.Add(b1); g.Children.Add(b2);
                Flash(b1, 2.2, idx * 0.35); Flash(b2, 1.7, 0.6 + idx * 0.2);
                Pulse(aura, 0.1, 0.5, 0.6);
                break;
            }
            default:   // ShadowMonarch: dark eye, violet glow, violet lightning
            {
                Ball("#FF14081F");
                var aura = Center(h * 0.80, "#FF8A2BFF"); aura.Opacity = 0.3;
                Center(h * 0.54, "#FFA24BFF");
                Center(h * 0.28, "#FFF2E3FF");
                var b1 = Bolt(h, "#FFD2A6FF", h * 0.60, -h * 0.26, 10);
                var b2 = Bolt(h, "#FFA24BFF", -h * 0.10, h * 0.46, -22, 0.9);
                var b3 = Bolt(h, "#FFE6CCFF", h * 0.30, -h * 0.36, -6, 0.6);
                g.Children.Add(b1); g.Children.Add(b2); g.Children.Add(b3);
                Flash(b1, 2.0, idx * 0.3); Flash(b2, 1.5, 0.5 + idx * 0.25); Flash(b3, 2.6, 1.1);
                Pulse(aura, 0.12, 0.6, 0.8);
                break;
            }
        }
        return g;
    }
}
