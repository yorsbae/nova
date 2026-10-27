using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nova.App.Features.DynamicIsland;

/// <summary>What is drawn on the iris, besides the normal pupil and shine. Every kind sits inside the same almond-shaped lids.</summary>
internal enum EyeKind { Plain, Sharingan, Rinnegan, Ghoul, Void, Alchemy, Web, Galaxy, Zone, Monarch }

/// <summary>How an eye is coloured and shaped. One look per eye style (see <see cref="EyeLooks"/>).</summary>
internal sealed record EyeLook(
    string Iris1, string Iris2,
    string Sclera = "#FFF8F8FF", string Lid = "#FF1B1226", string Pupil = "#FF0B0B12",
    string? Aura = null,
    double Slant = 0, double IrisW = 0.52, double PupilSize = 0.40,
    bool Slit = false, bool Wing = true, bool Lashes = true, bool Bags = false, bool Sparkle = false, bool Highlights = true,
    EyeKind Kind = EyeKind.Plain, string? Accent = null, string? Halo = null);

internal static class EyeLooks
{
    public static readonly EyeLook AnimeAura = new("#FFE0FFFF", "#FF1AA3D9", Aura: "#FF4FE8FF", Sparkle: true, IrisW: 0.54);      // glowing cyan
    public static readonly EyeLook Yandere = new("#FFFF6B8A", "#FF6E0016", Sclera: "#FFF4E6EC", Lid: "#FF2A0008", Pupil: "#FF1A0005",
        Aura: "#FFFF1744", Slant: 5, IrisW: 0.60, PupilSize: 0.20, Bags: true, Highlights: false);                               // tiny pupil, no shine, red aura
    public static readonly EyeLook Demon = new("#FFFFE27A", "#FFC4500C", Sclera: "#FF140808", Lid: "#FFB8400F", Pupil: "#FF050000",
        Aura: "#FFFF7A1A", Slant: 15, IrisW: 0.58, Slit: true, Bags: true, Highlights: false);                                   // black eye, gold slit, angry
    public static readonly EyeLook Hollow = new("#FFFFFFFF", "#FFA9B8FF", Sclera: "#FF050508", Lid: "#FF8F9AD0", Pupil: "#FF000000",
        Aura: "#FFB9C6FF", Slant: 8, IrisW: 0.50, PupilSize: 0, Bags: true, Highlights: false);                                  // black eye, glowing white iris

    // The special eyes: same anime lids and lashes, only the iris is different.
    static readonly EyeLook Sharingan = new("#FFE5271F", "#FF9E0707", Sclera: "#FFFFFFFF", Lid: "#FF0B0506", Pupil: "#FF0A0000",
        IrisW: 0.66, PupilSize: 0.12, Highlights: false, Kind: EyeKind.Sharingan, Accent: "#FFD81414", Halo: "#FFFF2A1A");        // red iris: three tomoe, then the turning Mangekyo
    static readonly EyeLook Rinnegan = new("#FFCFC4E6", "#FF9C90C4", Sclera: "#FFFFFFFF", Lid: "#FF0B0A12", Pupil: "#FF17102E",
        IrisW: 0.66, PupilSize: 0.12, Highlights: false, Kind: EyeKind.Rinnegan, Accent: "#FF5A4C86", Halo: "#FFFFFFFF");        // lilac iris, many fine rings rippling outward, tiny tomoe
    static readonly EyeLook GhoulLeft = new("#FFE4E7EC", "#FF9AA1AD", Sclera: "#FFEDEDED", Lid: "#FF1E1E22", Pupil: "#FF222222",
        IrisW: 0.52, PupilSize: 0.28);                                                                                           // an ordinary pale eye
    static readonly EyeLook GhoulRight = new("#FFFF5560", "#FFC4101A", Sclera: "#FF070707", Lid: "#FF2A0A0C", Pupil: "#FF110000",
        Aura: "#FFFF1E2D", IrisW: 0.56, PupilSize: 0.30, Bags: true, Highlights: false, Kind: EyeKind.Ghoul);                    // black eye, glowing red iris that flickers
    static readonly EyeLook Void = new("#FF2A0000", "#FF0C0000", Sclera: "#FF050505", Lid: "#FF2A0A0A", Pupil: "#FF000000",
        Aura: "#FFFF2A2A", IrisW: 0.60, PupilSize: 0, Highlights: false, Kind: EyeKind.Void, Accent: "#FFFF2A2A");                 // black eye, one red ring
    static readonly EyeLook AlchemyLeft = new("#FF9CC4F4", "#FF2F66C0", Sclera: "#FFFAFAFA", IrisW: 0.46, PupilSize: 0.34);       // small blue iris
    static readonly EyeLook AlchemyRight = new("#FFFFE8E8", "#FFD01F1F", Sclera: "#FFFAFAFA", IrisW: 0.66, PupilSize: 0,
        Highlights: false, Kind: EyeKind.Alchemy, Accent: "#FFD01F1F");                                                          // a red transmutation circle that turns
    static readonly EyeLook Web = new("#FF9CC2FF", "#FF3D7BFF", Sclera: "#FFF7F7F7", Lid: "#FF0E1A3A", Pupil: "#FF0A1A44",
        IrisW: 0.62, PupilSize: 0.16, Highlights: false, Kind: EyeKind.Web, Accent: "#FFE4EEFF");                                // blue iris with a shimmering web
    static readonly EyeLook CatLeft = new("#FFFFB0CF", "#FFFF4D8D", Sclera: "#FFFAFAFA", IrisW: 0.62, Slit: true);                // pink, slit pupil
    static readonly EyeLook CatRight = new("#FFFFE08A", "#FFFFA31A", Sclera: "#FFFAFAFA", IrisW: 0.62, Slit: true);              // amber, slit pupil
    static readonly EyeLook Galaxy = new("#FFC77DFF", "#FF8E2DE2", Sclera: "#FFF3EEFA", Lid: "#FF1A0F2E", Pupil: "#FF1A0F2E",
        IrisW: 0.60, PupilSize: 0, Highlights: false, Kind: EyeKind.Galaxy);                                                     // purple iris with a turning star
    static readonly EyeLook Zone = new("#FFE8FAFF", "#FF59C8FF", Sclera: "#FFF2FAFF", Lid: "#FF0E2A40", Pupil: "#FF06324F",
        Aura: "#FF59C8FF", IrisW: 0.56, PupilSize: 0.30, Kind: EyeKind.Zone, Accent: "#FFB5EEFF");                               // light blue, crackling with lightning
    static readonly EyeLook Monarch = new("#FFE9D0FF", "#FFA24BFF", Sclera: "#FF14081F", Lid: "#FF2A1048", Pupil: "#FFF2E3FF",
        Aura: "#FF8A2BFF", Slant: 6, IrisW: 0.58, PupilSize: 0.30, Highlights: false, Kind: EyeKind.Monarch, Accent: "#FFD2A6FF"); // dark eye, violet lightning

    /// <summary>The look of one eye of a style; <paramref name="idx"/> is 0 (left) or 1 (right) because some characters have two different eyes. Null for Pet.</summary>
    public static EyeLook? For(Nova.Core.Configuration.IslandFaceStyle style, int idx) => style switch
    {
        Nova.Core.Configuration.IslandFaceStyle.AnimeAura => AnimeAura,
        Nova.Core.Configuration.IslandFaceStyle.Yandere => Yandere,
        Nova.Core.Configuration.IslandFaceStyle.Demon => Demon,
        Nova.Core.Configuration.IslandFaceStyle.Hollow => Hollow,
        Nova.Core.Configuration.IslandFaceStyle.Sharingan => Sharingan,
        Nova.Core.Configuration.IslandFaceStyle.Rinnegan => Rinnegan,
        Nova.Core.Configuration.IslandFaceStyle.Ghoul => idx == 0 ? GhoulLeft : GhoulRight,
        Nova.Core.Configuration.IslandFaceStyle.Void => Void,
        Nova.Core.Configuration.IslandFaceStyle.Alchemy => idx == 0 ? AlchemyLeft : AlchemyRight,
        Nova.Core.Configuration.IslandFaceStyle.Web => Web,
        Nova.Core.Configuration.IslandFaceStyle.Cat => idx == 0 ? CatLeft : CatRight,
        Nova.Core.Configuration.IslandFaceStyle.Galaxy => Galaxy,
        Nova.Core.Configuration.IslandFaceStyle.Zone => Zone,
        Nova.Core.Configuration.IslandFaceStyle.ShadowMonarch => Monarch,
        _ => null
    };
}

/// <summary>What the eyes are "feeling". The Island picks one from what is going on (battery, Pomodoro, music, notices, the hour).</summary>
internal enum EyeMood { Normal, Sleepy, Focus, Alert, Music }

/// <summary>
/// One real-looking anime eye, drawn by hand every time something changes. Unlike a picture that is squashed to blink, the upper lid
/// sweeps down over the iris and the lower lid rises to meet it; the iris and pupil move inside the lids to look at things; the lashes
/// droop as the eye closes; the aura breathes. All of it is driven by a few animatable numbers (<see cref="Blink"/>, <see cref="GazeX"/>,
/// <see cref="Glow"/> ...), so WPF's animation system does the timing and nothing is redrawn while the eye is still.
/// </summary>
internal sealed class RealEye : FrameworkElement
{
    static DependencyProperty Reg(string name, double def)
        => DependencyProperty.Register(name, typeof(double), typeof(RealEye), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OpenProperty = Reg(nameof(Open), 1.0);          // how wide the eye is open by mood: 0 closed … 1 normal … 1.2 wide
    public static readonly DependencyProperty BlinkProperty = Reg(nameof(Blink), 0.0);        // 0 open … 1 shut (animated by blinks)
    public static readonly DependencyProperty SquintProperty = Reg(nameof(Squint), 0.0);      // lower lid pushed up (smile / focus)
    public static readonly DependencyProperty TiltProperty = Reg(nameof(Tilt), 0.0);          // extra lid angle in degrees (+ = angrier)
    public static readonly DependencyProperty PupilScaleProperty = Reg(nameof(PupilScale), 1.0);
    public static readonly DependencyProperty GlowProperty = Reg(nameof(Glow), 1.0);          // aura strength by mood
    public static readonly DependencyProperty PulseProperty = Reg(nameof(Pulse), 1.0);        // aura breathing, 0.5 … 1
    public static readonly DependencyProperty TwinkleProperty = Reg(nameof(Twinkle), 1.0);    // star sparkle size, 0.3 … 1
    public static readonly DependencyProperty GazeXProperty = Reg(nameof(GazeX), 0.0);        // −1 left … 1 right (as seen on screen)
    public static readonly DependencyProperty GazeYProperty = Reg(nameof(GazeY), 0.0);        // −1 up … 1 down
    public static readonly DependencyProperty SpinProperty = Reg(nameof(Spin), 0.0);          // degrees: tomoe, transmutation circle, web, star
    public static readonly DependencyProperty BreatheProperty = Reg(nameof(Breathe), 1.0);    // Rinnegan rings swelling, 1 … 1.14
    public static readonly DependencyProperty FlickerProperty = Reg(nameof(Flicker), 1.0);    // the ghoul eye's glitching iris, 0 … 1
    public static readonly DependencyProperty BoltProperty = Reg(nameof(Bolt), 0.0);          // lightning clock, 0 … 1 and round again
    public static readonly DependencyProperty VanishProperty = Reg(nameof(Vanish), 0.0);      // 0 visible … 1 gone (below 0 = pops back a little too big)
    public static readonly DependencyProperty PopProperty = Reg(nameof(Pop), 1.0);            // whole-eye size, for the little surprise pop
    public static readonly DependencyProperty EvolveProperty = Reg(nameof(Evolve), 0.0);      // Sharingan: 0 three tomoe … 1 Mangekyo

    public double Open { get => (double)GetValue(OpenProperty); set => SetValue(OpenProperty, value); }
    public double Blink { get => (double)GetValue(BlinkProperty); set => SetValue(BlinkProperty, value); }
    public double Squint { get => (double)GetValue(SquintProperty); set => SetValue(SquintProperty, value); }
    public double Tilt { get => (double)GetValue(TiltProperty); set => SetValue(TiltProperty, value); }
    public double PupilScale { get => (double)GetValue(PupilScaleProperty); set => SetValue(PupilScaleProperty, value); }
    public double Glow { get => (double)GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    public double Pulse { get => (double)GetValue(PulseProperty); set => SetValue(PulseProperty, value); }
    public double Twinkle { get => (double)GetValue(TwinkleProperty); set => SetValue(TwinkleProperty, value); }
    public double GazeX { get => (double)GetValue(GazeXProperty); set => SetValue(GazeXProperty, value); }
    public double GazeY { get => (double)GetValue(GazeYProperty); set => SetValue(GazeYProperty, value); }
    public double Spin { get => (double)GetValue(SpinProperty); set => SetValue(SpinProperty, value); }
    public double Breathe { get => (double)GetValue(BreatheProperty); set => SetValue(BreatheProperty, value); }
    public double Flicker { get => (double)GetValue(FlickerProperty); set => SetValue(FlickerProperty, value); }
    public double Bolt { get => (double)GetValue(BoltProperty); set => SetValue(BoltProperty, value); }
    public double Vanish { get => (double)GetValue(VanishProperty); set => SetValue(VanishProperty, value); }
    public double Pop { get => (double)GetValue(PopProperty); set => SetValue(PopProperty, value); }
    public double Evolve { get => (double)GetValue(EvolveProperty); set => SetValue(EvolveProperty, value); }

    readonly EyeLook _look;
    readonly bool _mirror;
    readonly int _idx;
    readonly double _w, _h;
    readonly Brush _sclera, _iris, _shadow, _lid, _auraOuter, _auraInner, _pupil, _white, _whiteSoft;
    readonly Pen _irisPen, _bagPen, _creasePen, _ringPen, _accentPen, _lightPen, _boltPen;
    readonly Brush _accent;
    readonly Pen _tailPen;
    readonly Geometry _bolt;
    readonly Geometry? _wing;
    readonly Geometry _star, _tomoe, _blade;
    readonly Pen _haloPen;

    public RealEye(EyeLook look, double h, int idx)
    {
        _look = look; _idx = idx; _mirror = idx == 1; _h = h; _w = h * 1.60;   // long, sharp anime eye
        Width = _w; Height = _h;
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(h * 0.04, 0, h * 0.04, 0);
        RenderTransformOrigin = new Point(0.5, 0.5);

        var lid = Parse(look.Lid); var iris1 = Parse(look.Iris1); var iris2 = Parse(look.Iris2); var sclera = Parse(look.Sclera);

        _sclera = Fz(new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops = { new GradientStop(Darken(sclera, 0.78), 0), new GradientStop(sclera, 0.45), new GradientStop(sclera, 1) }
        });
        _iris = Fz(new RadialGradientBrush
        {
            Center = new Point(0.5, 0.62), GradientOrigin = new Point(0.5, 0.80), RadiusX = 0.62, RadiusY = 0.62,
            GradientStops = { new GradientStop(iris1, 0), new GradientStop(iris2, 0.78), new GradientStop(Darken(iris2, 0.55), 1) }
        });
        _irisPen = new Pen(Fz(new SolidColorBrush(Darken(iris2, 0.45))), Math.Max(1, h * 0.035)); _irisPen.Freeze();
        _shadow = Fz(new LinearGradientBrush(Color.FromArgb(0xB4, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90));
        _lid = Fz(new SolidColorBrush(lid));
        _pupil = Fz(new SolidColorBrush(Parse(look.Pupil)));
        _white = Fz(new SolidColorBrush(Colors.White));
        _whiteSoft = Fz(new SolidColorBrush(Color.FromArgb(0xD8, 255, 255, 255)));
        _tailPen = new Pen(_pupil, Math.Max(1, h * 0.045)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; _tailPen.Freeze();
        var accent = Parse(look.Accent ?? look.Iris2);
        _accent = Fz(new SolidColorBrush(accent));
        _ringPen = new Pen(Fz(new SolidColorBrush(Alpha(Darken(iris2, 0.5), 0xD0))), Math.Max(1, h * 0.03)); _ringPen.Freeze();
        _accentPen = new Pen(_accent, Math.Max(1.2, h * 0.045)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; _accentPen.Freeze();
        _lightPen = new Pen(Fz(new SolidColorBrush(Alpha(accent, 0xC0))), Math.Max(1, h * 0.028)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; _lightPen.Freeze();
        _boltPen = new Pen(_accent, Math.Max(1.2, h * 0.055)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; _boltPen.Freeze();
        _bagPen = new Pen(Fz(new SolidColorBrush(Alpha(lid, 0x66))), Math.Max(1.5, h * 0.09)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; _bagPen.Freeze();
        _creasePen = new Pen(Fz(new SolidColorBrush(Alpha(lid, 0x70))), Math.Max(1, h * 0.03)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; _creasePen.Freeze();

        if (look.Aura is not null)
        {
            var a = Parse(look.Aura);
            _auraOuter = Fz(new RadialGradientBrush { GradientStops = { new GradientStop(Alpha(a, 0x70), 0), new GradientStop(Alpha(a, 0x2A), 0.55), new GradientStop(Alpha(a, 0x00), 1) } });
            _auraInner = Fz(new RadialGradientBrush { GradientStops = { new GradientStop(Alpha(a, 0xC8), 0), new GradientStop(Alpha(a, 0x60), 0.6), new GradientStop(Alpha(a, 0x00), 1) } });
        }
        else { _auraOuter = Brushes.Transparent; _auraInner = Brushes.Transparent; }

        if (look.Wing)
        {
            // the lash line flicks out past the outer corner in a long, sharp tail
            var sg = new StreamGeometry();
            using (var c = sg.Open())
            {
                c.BeginFigure(new Point(0.05 * _w, 0.40 * _h), true, true);
                c.QuadraticBezierTo(new Point(-0.03 * _w, 0.30 * _h), new Point(-0.11 * _w, 0.10 * _h), true, false);
                c.QuadraticBezierTo(new Point(0.08 * _w, 0.17 * _h), new Point(0.26 * _w, 0.07 * _h), true, false);
            }
            sg.Freeze(); _wing = sg;
        }

        // the soft outline that glows around the lids (white, or the look's own colour)
        var halo = Parse(look.Halo ?? look.Aura ?? "#FFFFFFFF");
        _haloPen = new Pen(Fz(new SolidColorBrush(halo)), 1) { LineJoin = PenLineJoin.Round }; _haloPen.Freeze();

        // one tomoe in unit size: a round head at the origin and a tail that curves clockwise along the circle it sits on
        var tomoe = new StreamGeometry();
        using (var c = tomoe.Open())
        {
            c.BeginFigure(new Point(0.15, -1.0), true, true);
            c.BezierTo(new Point(1.0, -1.1), new Point(1.0, 0.0), new Point(0.95, 0.35), true, false);   // head's top, round the right side
            c.BezierTo(new Point(1.9, -0.55), new Point(2.7, -0.55), new Point(3.6, 0.9), true, false);  // outer edge of the tail out to its tip
            c.BezierTo(new Point(2.5, -0.05), new Point(1.6, 0.30), new Point(0.55, 0.92), true, false); // back along the inner edge
            c.BezierTo(new Point(-0.6, 1.1), new Point(-1.2, -0.3), new Point(0.15, -1.0), true, false); // underside of the head
        }
        tomoe.Freeze(); _tomoe = tomoe;

        // one Mangekyo blade (Sasuke's eternal): a pointed crown with curved sides, three of them turn around the pupil
        var blade = new StreamGeometry();
        using (var c = blade.Open())
        {
            c.BeginFigure(new Point(0, -0.20), true, true);
            c.QuadraticBezierTo(new Point(-0.46, -0.22), new Point(-0.64, -0.60), true, false);
            c.QuadraticBezierTo(new Point(-0.26, -0.52), new Point(0, -0.84), true, false);
            c.QuadraticBezierTo(new Point(0.26, -0.52), new Point(0.64, -0.60), true, false);
            c.QuadraticBezierTo(new Point(0.46, -0.22), new Point(0, -0.20), true, false);
        }
        blade.Freeze(); _blade = blade;

        // 4-point star for the sparkle
        var star = new StreamGeometry();
        using (var c = star.Open())
        {
            const double inner = 0.28;
            c.BeginFigure(new Point(0, -1), true, true);
            c.LineTo(new Point(inner * 0.7, -inner * 0.7), true, false); c.LineTo(new Point(1, 0), true, false);
            c.LineTo(new Point(inner * 0.7, inner * 0.7), true, false); c.LineTo(new Point(0, 1), true, false);
            c.LineTo(new Point(-inner * 0.7, inner * 0.7), true, false); c.LineTo(new Point(-1, 0), true, false);
            c.LineTo(new Point(-inner * 0.7, -inner * 0.7), true, false);
        }
        star.Freeze(); _star = star;

        // a zig-zag lightning bolt (drawn for the Zone and ShadowMonarch eyes), in units of the eye's height
        var bolt = new StreamGeometry();
        using (var c = bolt.Open())
        {
            c.BeginFigure(new Point(0, 0), false, false);
            c.PolyLineTo(new[] { new Point(0.10 * h, 0.18 * h), new Point(0.02 * h, 0.22 * h), new Point(0.14 * h, 0.46 * h), new Point(0.06 * h, 0.50 * h), new Point(0.20 * h, 0.80 * h) }, true, true);
        }
        bolt.Freeze(); _bolt = bolt;
    }

    // ---- small helpers ----
    static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
    static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    static T Fz<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static double Lerp(double a, double b, double t) => a + (b - a) * t;
    static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    static Point Bezier(Point a, Point b, Point c, Point d, double t)
    {
        double u = 1 - t;
        return new Point(
            u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X,
            u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y);
    }

    static StreamGeometry Almond(Point a, Point u1, Point u2, Point b, Point l1, Point l2)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(a, true, true);
            c.BezierTo(u1, u2, b, true, false);
            c.BezierTo(l1, l2, a, true, false);
        }
        g.Freeze(); return g;
    }

    static StreamGeometry Curve(Point a, Point p1, Point p2, Point b)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(a, false, false);
            c.BezierTo(p1, p2, b, true, false);
        }
        g.Freeze(); return g;
    }

    // ---- drawing ----
    protected override void OnRender(DrawingContext dc)
    {
        double w = _w, h = _h;

        // vanishing and popping: the whole eye fades and shrinks (or swells a little) around its centre
        double vanish = Clamp(Vanish, -0.3, 1);
        double vis = 1 - Clamp(vanish, 0, 1);
        if (vis < 0.01) return;
        bool wrap = vanish != 0 || Math.Abs(Pop - 1) > 0.001;
        if (wrap)
        {
            double sc = Math.Max(0.05, Pop * (1 - 0.55 * vanish));
            dc.PushOpacity(vis);
            dc.PushTransform(new ScaleTransform(sc, sc, w / 2, h / 2));
        }

        // how open: mood opening, closed down by the blink
        double o = Clamp(Open * (1 - Clamp(Blink, -0.15, 1)), 0, 1.25);
        double om = Math.Min(o, 1), over = Math.Max(0, o - 1);
        double sq = Clamp(Squint, 0, 1);
        double gx = Clamp(GazeX, -1, 1) * (_mirror ? -1 : 1), gy = Clamp(GazeY, -1, 1);
        double glow = Math.Max(0, Glow) * (0.55 + 0.45 * Clamp(Pulse, 0, 1.2));

        // the aura sits behind the eye and is not tilted
        if (_look.Aura is not null && glow > 0.02)
        {
            dc.PushOpacity(Math.Min(1, glow));
            dc.DrawEllipse(_auraOuter, null, new Point(w / 2, h / 2), w * 0.80, h * 0.78);
            dc.DrawEllipse(_auraInner, null, new Point(w / 2, h * 0.52), w * 0.56, h * 0.50);
            dc.Pop();
        }

        if (_mirror) dc.PushTransform(new MatrixTransform(-1, 0, 0, 1, w, 0));
        dc.PushTransform(new RotateTransform(_look.Slant + Tilt, w / 2, h / 2));

        // the two lid curves: the upper one sweeps down as the eye closes, the lower one rises
        var A = new Point(0.0 * w, 0.38 * h);      // outer corner: sharp, high
        var B = new Point(1.0 * w, 0.64 * h);      // inner corner: low
        double lowY = h * (0.92 - 0.20 * sq - 0.10 * (1 - om));
        var U1 = new Point(Lerp(0.30 * w, 0.24 * w, om), Lerp(lowY, -0.10 * h - over * 0.3 * h, om));
        var U2 = new Point(Lerp(0.78 * w, 0.64 * w, om), Lerp(lowY, -0.04 * h - over * 0.3 * h, om));
        var L1 = new Point(0.80 * w, lowY - 0.02 * h);
        var L2 = new Point(0.34 * w, lowY - 0.10 * h);

        // the glow that hugs the lids (stacked soft outlines, fainter the further out)
        {
            var hg = Almond(A, U1, U2, B, L1, L2);
            double hv = Clamp(Glow, 0, 1.6) * (0.75 + 0.25 * Clamp(Pulse, 0, 1.2)) * (0.30 + 0.70 * om);
            for (int i = 4; i >= 1; i--)
            {
                var pen = new Pen(_haloPen.Brush, h * 0.07 * i) { LineJoin = PenLineJoin.Round };
                dc.PushOpacity(Math.Min(1, 0.16 * hv / (1 + 0.35 * i)));
                dc.DrawGeometry(null, pen, hg);
                dc.Pop();
            }
        }

        // eye bags: a dark crescent under the lid for the creepy looks
        if (_look.Bags)
        {
            dc.PushTransform(new TranslateTransform(0, h * 0.07));
            dc.DrawGeometry(null, _bagPen, Curve(B, L1, L2, A));
            dc.Pop();
        }

        // inside the lids
        var eyeGeo = Almond(A, U1, U2, B, L1, L2);
        if (om > 0.03)
        {
            dc.PushClip(eyeGeo);
            dc.DrawGeometry(_sclera, null, eyeGeo);

            double irx = h * 0.78 * _look.IrisW, iry = irx * (_look.Kind == EyeKind.Plain ? 1.06 : 1.0);   // round iris, cut by the lids
            double cx = 0.54 * w + gx * w * 0.13, cy = 0.46 * h + gy * h * 0.10;
            var ctr = new Point(cx, cy);
            bool flick = _look.Kind == EyeKind.Ghoul;
            if (flick) dc.PushOpacity(Clamp(Flicker, 0, 1));
            dc.DrawEllipse(_iris, _irisPen, ctr, irx, iry);
            DrawDecor(dc, ctr, irx, iry);

            double ps = Clamp(PupilScale, 0.3, 3.5);
            if (_look.Slit)
                dc.DrawEllipse(_pupil, null, new Point(cx, cy + 0.02 * h), Math.Min(irx * 0.9, irx * 0.13 * ps), iry * 0.95);
            else if (_look.PupilSize > 0 && _look.Kind == EyeKind.Plain)
                dc.DrawEllipse(_pupil, null, new Point(cx, cy + 0.04 * h), Math.Min(irx * 0.95, irx * _look.PupilSize * ps), Math.Min(iry * 0.95, iry * Math.Max(0.28, _look.PupilSize * 1.2) * ps));
            else if (_look.PupilSize > 0 && _look.Kind != EyeKind.Sharingan && _look.Kind != EyeKind.Rinnegan)
            {
                double pr = Math.Min(irx, iry) * _look.PupilSize * Math.Min(ps, 1.6);   // special eyes keep a round pupil
                dc.DrawEllipse(_pupil, null, ctr, pr, pr);
            }
            if (flick) dc.Pop();

            // the lashes shade the top of the eye (under the shine, so the shine stays white)
            double peakY = Bezier(A, U1, U2, B, 0.5).Y;
            dc.DrawRectangle(_shadow, null, new Rect(0, peakY - 0.05 * h, w, 0.50 * h));

            if (_look.Highlights)
            {
                dc.DrawEllipse(_white, null, new Point(cx - irx * 0.45, cy - iry * 0.42), h * 0.105, h * 0.105);
                dc.DrawEllipse(_whiteSoft, null, new Point(cx + irx * 0.28, cy + iry * 0.38), h * 0.045, h * 0.045);
            }
            if (_look.Sparkle)
            {
                double r = h * 0.17 * (0.35 + 0.65 * Clamp(Twinkle, 0, 1));
                dc.PushTransform(new TranslateTransform(cx - irx * 0.35, cy - iry * 0.45));
                dc.PushTransform(new ScaleTransform(r, r));
                dc.DrawGeometry(_white, null, _star);
                dc.Pop(); dc.Pop();
            }
            dc.Pop();   // clip
        }

        // double-eyelid crease above the lid; it fades as the eye closes
        if (om > 0.2)
        {
            dc.PushOpacity(om);
            dc.PushTransform(new TranslateTransform(0, -0.13 * h * om));
            dc.DrawGeometry(null, _creasePen, Curve(A, U1, U2, B));
            dc.Pop(); dc.Pop();
        }

        // the thick upper lash line (thicker when shut), the wing at the outer corner, and lashes that droop as the eye closes
        var lidPen = new Pen(_lid, Math.Max(1.8, h * (0.10 + 0.04 * (1 - om)))) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, lidPen, Curve(A, U1, U2, B));
        if (_wing is not null) dc.DrawGeometry(_lid, null, _wing);

        if (_look.Lashes)
        {
            var lashPen = new Pen(_lid, Math.Max(1.1, h * 0.045)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            foreach (var (t, len) in new[] { (0.04, 0.26), (0.10, 0.30), (0.17, 0.26), (0.25, 0.24), (0.34, 0.20), (0.44, 0.16), (0.56, 0.12) })
            {
                var p = Bezier(A, U1, U2, B, t); var q = Bezier(A, U1, U2, B, t + 0.02);
                double dx = q.X - p.X, dy = q.Y - p.Y, l = Math.Max(0.001, Math.Sqrt(dx * dx + dy * dy));
                double nx = dy / l, ny = -dx / l;                        // the normal that points up
                double side = om * 2 - 1;                               // +1 open: lashes up … −1 shut: lashes droop down
                double ox = nx * side - 0.85, oy = ny * side;   // swept back toward the outer corner
                double ol = Math.Max(0.001, Math.Sqrt(ox * ox + oy * oy)); ox /= ol; oy /= ol;
                dc.DrawLine(lashPen, p, new Point(p.X + ox * h * len, p.Y + oy * h * len));
            }
        }

        DrawBolts(dc, h);

        dc.Pop();                      // rotation
        if (_mirror) dc.Pop();         // mirror
        if (wrap) { dc.Pop(); dc.Pop(); }   // size, opacity
    }

    static readonly double[] RinneganRings = { 0.95, 0.86, 0.77, 0.68, 0.59, 0.50, 0.41 };
    static readonly double[] WebRings = { 0.85, 0.58, 0.32 };

    /// <summary>What makes the special eyes special: drawn on the iris, under the pupil's shine and inside the lids.</summary>
    void DrawDecor(DrawingContext dc, Point c, double irx, double iry)
    {
        double R = Math.Min(irx, iry);
        switch (_look.Kind)
        {
            case EyeKind.Sharingan:   // three tomoe that bloom into the turning Mangekyo
            {
                double ev = Clamp(Evolve, 0, 1);
                // the dark rim of the iris
                dc.DrawEllipse(null, _irisPen, c, R * 0.97, R * 0.97);
                if (ev < 0.999)
                {
                    dc.PushOpacity(1 - ev);
                    double ring = R * 0.60, tr = R * 0.17;
                    dc.DrawEllipse(null, _ringPen, c, ring, ring);
                    for (int i = 0; i < 3; i++)
                    {
                        dc.PushTransform(new TranslateTransform(c.X, c.Y));
                        dc.PushTransform(new RotateTransform(Spin * 0.5 + i * 120));
                        dc.PushTransform(new TranslateTransform(0, -ring));
                        dc.PushTransform(new ScaleTransform(tr, tr));
                        dc.DrawGeometry(_pupil, null, _tomoe);
                        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
                    }
                    dc.Pop();
                }
                if (ev > 0.001)
                {
                    dc.PushOpacity(ev);
                    double grow = 0.55 + 0.45 * ev;                       // the pattern swells out of the pupil as it blooms
                    dc.DrawEllipse(_pupil, null, c, R * 0.80 * grow, R * 0.80 * grow);   // black disc the blades sit on
                    for (int i = 0; i < 3; i++)
                    {
                        dc.PushTransform(new TranslateTransform(c.X, c.Y));
                        dc.PushTransform(new RotateTransform(-Spin + i * 120 + (1 - ev) * 90));
                        dc.PushTransform(new ScaleTransform(R * 0.80 * grow, R * 0.80 * grow));
                        dc.DrawGeometry(_accent, null, _blade);
                        dc.Pop(); dc.Pop(); dc.Pop();
                    }
                    dc.DrawEllipse(_pupil, _accentPen, c, R * 0.17, R * 0.17);          // the pupil with its red ring
                    dc.Pop();
                }
                break;
            }
            case EyeKind.Rinnegan:   // many fine rings rippling outward, a tiny pupil and small tomoe
            {
                double ph = Spin * Math.PI / 90;                              // two ripples per turn of Spin
                for (int i = 0; i < RinneganRings.Length; i++)
                {
                    double f = RinneganRings[i] + 0.022 * Math.Sin(ph - i * 0.9);
                    dc.DrawEllipse(null, _ringPen, c, R * f, R * f);
                }
                double ring = R * 0.30, tr = R * 0.075;
                for (int i = 0; i < 3; i++)
                {
                    dc.PushTransform(new TranslateTransform(c.X, c.Y));
                    dc.PushTransform(new RotateTransform(Spin + i * 120));
                    dc.PushTransform(new TranslateTransform(0, -ring));
                    dc.PushTransform(new ScaleTransform(tr, tr));
                    dc.DrawGeometry(_pupil, null, _tomoe);
                    dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
                }
                dc.DrawEllipse(_pupil, null, c, R * 0.11, R * 0.11);
                break;
            }
            case EyeKind.Void:   // one red ring that glows slowly
            {
                dc.PushOpacity(Clamp(Pulse, 0.3, 1));
                dc.DrawEllipse(null, _accentPen, c, R * 0.62, R * 0.62);
                dc.DrawEllipse(null, _lightPen, c, R * 0.88, R * 0.88);
                dc.DrawEllipse(_accent, null, c, R * 0.14, R * 0.14);
                dc.Pop();
                break;
            }
            case EyeKind.Alchemy:   // a transmutation circle with a turning hexagram
            {
                dc.DrawEllipse(null, _accentPen, c, R * 0.80, R * 0.80);
                dc.PushTransform(new RotateTransform(Spin, c.X, c.Y));
                for (int t = 0; t < 2; t++)
                    for (int k = 0; k < 3; k++)
                    {
                        double a1 = (t * 60 + k * 120) * Math.PI / 180, a2 = (t * 60 + (k + 1) * 120) * Math.PI / 180;
                        dc.DrawLine(_accentPen, new Point(c.X + R * 0.80 * Math.Sin(a1), c.Y - R * 0.80 * Math.Cos(a1)),
                                                new Point(c.X + R * 0.80 * Math.Sin(a2), c.Y - R * 0.80 * Math.Cos(a2)));
                    }
                dc.Pop();
                break;
            }
            case EyeKind.Web:   // a shimmering web of rings and threads
            {
                dc.PushTransform(new RotateTransform(Spin, c.X, c.Y));
                foreach (var f in WebRings) dc.DrawEllipse(null, _lightPen, c, R * f, R * f);
                dc.DrawLine(_lightPen, new Point(c.X - R * 0.9, c.Y), new Point(c.X + R * 0.9, c.Y));
                dc.DrawLine(_lightPen, new Point(c.X, c.Y - R * 0.9), new Point(c.X, c.Y + R * 0.9));
                dc.Pop();
                break;
            }
            case EyeKind.Galaxy:   // a turning star that twinkles
            {
                double sr = R * 0.95 * (0.55 + 0.45 * Clamp(Twinkle, 0, 1));
                dc.PushTransform(new TranslateTransform(c.X, c.Y));
                dc.PushTransform(new RotateTransform(Spin));
                dc.PushTransform(new ScaleTransform(sr, sr));
                dc.DrawGeometry(_white, null, _star);
                dc.Pop(); dc.Pop(); dc.Pop();
                break;
            }
        }
    }

    static double Frac(double x) => x - Math.Floor(x);

    /// <summary>One flash of a bolt: on, a flicker, on again, then dark until the next round (<paramref name="p"/> is 0 … 1 through the round).</summary>
    static double BoltAlpha(double p) => p < 0.52 ? 0 : p < 0.58 ? 1 : p < 0.62 ? 0.15 : p < 0.72 ? 1 : 0;

    void OneBolt(DrawingContext dc, double x, double y, double angle, double scale, double alpha)
    {
        if (alpha <= 0.01) return;
        dc.PushOpacity(alpha);
        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new RotateTransform(angle));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(null, _boltPen, _bolt);
        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
    }

    /// <summary>Lightning around the Zone and ShadowMonarch eyes: each bolt flashes on its own beat, like crackling electricity.</summary>
    void DrawBolts(DrawingContext dc, double h)
    {
        double t = Bolt, o = _idx * 0.15;
        if (_look.Kind == EyeKind.Zone)
        {
            OneBolt(dc, 0.62 * h, -0.24 * h, 8, 1.0, BoltAlpha(Frac(t + o)));
            OneBolt(dc, -0.06 * h, 0.50 * h, -18, 0.8, BoltAlpha(Frac(t + 0.37 + o)));
        }
        else if (_look.Kind == EyeKind.Monarch)
        {
            OneBolt(dc, 0.60 * h, -0.26 * h, 10, 1.0, BoltAlpha(Frac(t + o)));
            OneBolt(dc, -0.10 * h, 0.46 * h, -22, 0.9, BoltAlpha(Frac(t + 0.41 + o)));
            OneBolt(dc, 0.30 * h, -0.36 * h, -6, 0.6, BoltAlpha(Frac(2 * t + 0.20)));
        }
    }

    // ---- life: animations that run only while the face is on show ----

    /// <summary>The eye wakes up: it opens from a thin slit, a touch wider than normal, and settles (the opening of the reference clip).</summary>
    public void Awaken(double delay = 0)
    {
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, BeginTime = Sec(delay) };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.88, Sec(0)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0.88, Sec(0.25)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(-0.04, Sec(0.75), new CubicEase { EasingMode = EasingMode.EaseOut }));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, Sec(0.95), new SineEase { EasingMode = EasingMode.EaseInOut }));
        BeginAnimation(BlinkProperty, a);
    }

    /// <summary>One blink. <paramref name="slow"/> is for sleepy eyes.</summary>
    public void BlinkOnce(bool slow = false)
    {
        double down = slow ? 0.16 : 0.07, hold = slow ? 0.10 : 0.03, up = slow ? 0.28 : 0.12;
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, TimeSpan.FromSeconds(down), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromSeconds(down + hold)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromSeconds(down + hold + up), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        BeginAnimation(BlinkProperty, a);
    }

    static DoubleAnimation Loop(double from, double to, double seconds, bool autoReverse, IEasingFunction? ease = null)
        => new(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = autoReverse, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };

    /// <summary>Starts the style's own animation: the aura breathing, the sparkle twinkling, a slit pupil widening, tomoe circling, rings rippling, lightning.</summary>
    public void StartLife(double pulseSeconds)
    {
        if (_look.Aura is not null) BeginAnimation(PulseProperty, Loop(0.5, 1.0, pulseSeconds, true, new SineEase()));
        if (_look.Sparkle || _look.Kind == EyeKind.Galaxy) BeginAnimation(TwinkleProperty, Loop(0.3, 1.0, 0.9, true, new SineEase()));
        if (_look.Slit) BeginAnimation(PupilScaleProperty, Loop(1.0, 2.6, 1.6, true, new SineEase()));

        switch (_look.Kind)
        {
            case EyeKind.Sharingan:
            {
                BeginAnimation(SpinProperty, Loop(0, 360, 7.2, false));
                // 0.0-0.9 s three tomoe, bloom into the Mangekyo by 1.6 s, hold, back to tomoe at 6.2 s
                var ev = new DoubleAnimationUsingKeyFrames { Duration = Sec(7.2), RepeatBehavior = RepeatBehavior.Forever };
                foreach (var (v, t) in new[] { (0.0, 0.0), (0.0, 0.9), (1.0, 1.6), (1.0, 5.8), (0.0, 6.4), (0.0, 7.2) })
                    ev.KeyFrames.Add(new EasingDoubleKeyFrame(v, Sec(t), new SineEase { EasingMode = EasingMode.EaseInOut }));
                BeginAnimation(EvolveProperty, ev);
                break;
            }
            case EyeKind.Rinnegan: BeginAnimation(SpinProperty, Loop(0, 360, 7.2, false)); break;
            case EyeKind.Alchemy: BeginAnimation(SpinProperty, Loop(0, 360, 7, false)); break;
            case EyeKind.Web: BeginAnimation(SpinProperty, Loop(0, _idx == 1 ? -360 : 360, 9, false)); break;
            case EyeKind.Galaxy: BeginAnimation(SpinProperty, Loop(0, 360, 6, false)); break;
            case EyeKind.Zone: BeginAnimation(BoltProperty, Loop(0, 1, 2.0, false)); break;
            case EyeKind.Monarch: BeginAnimation(BoltProperty, Loop(0, 1, 2.2, false)); break;
            case EyeKind.Void: break;   // glows with the aura's Pulse
            case EyeKind.Ghoul:   // the red iris glitches now and then
            {
                var f = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(2.4), RepeatBehavior = RepeatBehavior.Forever };
                foreach (var (v, t) in new[] { (1.0, 0.0), (1.0, 1.2), (0.45, 1.26), (1.0, 1.32), (0.6, 1.4), (1.0, 1.46), (1.0, 2.4) })
                    f.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, TimeSpan.FromSeconds(t)));
                BeginAnimation(FlickerProperty, f);
                break;
            }
        }
    }

    /// <summary>Stops everything and puts the eye back to a calm, open, centred state.</summary>
    public void StopLife()
    {
        foreach (var p in new[] { BlinkProperty, PulseProperty, TwinkleProperty, PupilScaleProperty, OpenProperty, SquintProperty, TiltProperty, GlowProperty,
                                  SpinProperty, BreatheProperty, FlickerProperty, BoltProperty, VanishProperty, PopProperty, EvolveProperty })
            BeginAnimation(p, null);
        Blink = 0; Pulse = 1; Twinkle = 1; PupilScale = 1; Open = 1; Squint = 0; Tilt = 0; Glow = 1; GazeX = 0; GazeY = 0;
        Spin = 0; Breathe = 1; Flicker = 1; Bolt = 0; Vanish = 0; Pop = 1; Evolve = 0;
    }

    static TimeSpan Sec(double s) => TimeSpan.FromSeconds(s);

    /// <summary>The eye fades and shrinks away, stays gone for a moment, then pops back (a little too big first, then settling).</summary>
    public void VanishOnce(double delay = 0, double hiddenSeconds = 0.5)
    {
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, BeginTime = Sec(delay) };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, Sec(0.30), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, Sec(0.30 + hiddenSeconds)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, Sec(0.30 + hiddenSeconds + 0.40), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 }));
        BeginAnimation(VanishProperty, a);
    }

    /// <summary>A small surprise: the eye swells and settles back.</summary>
    public void PopOnce()
    {
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1.28, Sec(0.16), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, Sec(0.50), new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 6 }));
        BeginAnimation(PopProperty, a);
    }

    /// <summary>One eye closes for a moment (a wink).</summary>
    public void WinkOnce()
    {
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, Sec(0.10), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, Sec(0.55)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, Sec(0.75), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        BeginAnimation(BlinkProperty, a);
    }

    /// <summary>Eases the eye into a mood (about a third of a second).</summary>
    public void SetMood(EyeMood mood)
    {
        var (open, squint, tilt, pupil, glow) = mood switch
        {
            EyeMood.Sleepy => (0.55, 0.05, 0.0, 1.15, 0.5),
            EyeMood.Focus => (0.80, 0.12, 4.0, 0.85, 1.1),
            EyeMood.Alert => (1.18, 0.0, -3.0, 0.70, 1.6),
            EyeMood.Music => (0.92, 0.30, 0.0, 1.10, 1.5),
            _ => (1.0, 0.0, 0.0, 1.0, 1.0)
        };
        Ease(OpenProperty, open); Ease(SquintProperty, squint); Ease(TiltProperty, tilt); Ease(GlowProperty, glow);
        if (!_look.Slit) Ease(PupilScaleProperty, pupil);   // slit pupils keep their own widening
    }

    void Ease(DependencyProperty p, double to)
        => BeginAnimation(p, new DoubleAnimation(to, TimeSpan.FromSeconds(0.35)) { FillBehavior = FillBehavior.HoldEnd, EasingFunction = new SineEase() });
}
