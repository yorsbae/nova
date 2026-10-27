using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nova.App.Features.DynamicIsland;

/// <summary>How an eye is coloured and shaped. One look per eye style (see <see cref="EyeLooks"/>).</summary>
internal sealed record EyeLook(
    string Iris1, string Iris2,
    string Sclera = "#FFF8F8FF", string Lid = "#FF1B1226", string Pupil = "#FF0B0B12",
    string? Aura = null,
    double Slant = 0, double IrisW = 0.52, double PupilSize = 0.40,
    bool Slit = false, bool Wing = true, bool Lashes = true, bool Bags = false, bool Sparkle = false, bool Highlights = true);

internal static class EyeLooks
{
    public static readonly EyeLook AnimeA = new("#FF9BDCFF", "#FF2457D6");                                                       // calm blue
    public static readonly EyeLook AnimeB = new("#FFE3B8FF", "#FF6A28C4", Lid: "#FF140A22", IrisW: 0.50);                         // violet
    public static readonly EyeLook AnimeC = new("#FFC2FFDC", "#FF1C9A5E", Sparkle: true, IrisW: 0.56);                            // green, star sparkle
    public static readonly EyeLook AnimeAura = new("#FFE0FFFF", "#FF1AA3D9", Aura: "#FF4FE8FF", Sparkle: true, IrisW: 0.54);      // glowing cyan
    public static readonly EyeLook Yandere = new("#FFFF6B8A", "#FF6E0016", Sclera: "#FFF4E6EC", Lid: "#FF2A0008", Pupil: "#FF1A0005",
        Aura: "#FFFF1744", Slant: 5, IrisW: 0.60, PupilSize: 0.20, Bags: true, Highlights: false);                               // tiny pupil, no shine, red aura
    public static readonly EyeLook Demon = new("#FFFFE27A", "#FFC4500C", Sclera: "#FF140808", Lid: "#FFB8400F", Pupil: "#FF050000",
        Aura: "#FFFF7A1A", Slant: 15, IrisW: 0.58, Slit: true, Bags: true, Highlights: false);                                   // black eye, gold slit, angry
    public static readonly EyeLook Hollow = new("#FFFFFFFF", "#FFA9B8FF", Sclera: "#FF050508", Lid: "#FF8F9AD0", Pupil: "#FF000000",
        Aura: "#FFB9C6FF", Slant: 8, IrisW: 0.50, PupilSize: 0, Bags: true, Highlights: false);                                  // black eye, glowing white iris
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

    readonly EyeLook _look;
    readonly bool _mirror;
    readonly double _w, _h;
    readonly Brush _sclera, _iris, _shadow, _lid, _auraOuter, _auraInner, _pupil, _white, _whiteSoft;
    readonly Pen _irisPen, _bagPen, _creasePen;
    readonly Geometry? _wing;
    readonly Geometry _star;

    public RealEye(EyeLook look, double h, int idx)
    {
        _look = look; _mirror = idx == 1; _h = h; _w = h * 1.05;
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
            var sg = new StreamGeometry();
            using (var c = sg.Open())
            {
                c.BeginFigure(new Point(0.05 * _w, 0.46 * _h), true, true);
                c.QuadraticBezierTo(new Point(-0.06 * _w, 0.37 * _h), new Point(-0.17 * _w, 0.21 * _h), true, false);
                c.QuadraticBezierTo(new Point(0.04 * _w, 0.26 * _h), new Point(0.26 * _w, 0.12 * _h), true, false);
            }
            sg.Freeze(); _wing = sg;
        }

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
        // how open: mood opening, closed down by the blink
        double o = Clamp(Open * (1 - Clamp(Blink, 0, 1)), 0, 1.25);
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
        var A = new Point(0.02 * w, 0.50 * h);
        var B = new Point(0.98 * w, 0.66 * h);
        double lowY = h * (1.0 - 0.24 * sq - 0.10 * (1 - om));
        var U1 = new Point(Lerp(0.30 * w, 0.20 * w, om), Lerp(lowY, 0.0 - over * 0.3 * h, om));
        var U2 = new Point(Lerp(0.78 * w, 0.72 * w, om), Lerp(lowY, -0.02 * h - over * 0.3 * h, om));
        var L1 = new Point(0.78 * w, lowY);
        var L2 = new Point(0.30 * w, lowY);

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

            double irx = w * _look.IrisW * 0.5, iry = h * 0.56;
            double cx = 0.52 * w + gx * w * 0.15, cy = 0.50 * h + gy * h * 0.10;
            dc.DrawEllipse(_iris, _irisPen, new Point(cx, cy), irx, iry);

            double ps = Clamp(PupilScale, 0.3, 3.5);
            if (_look.Slit)
                dc.DrawEllipse(_pupil, null, new Point(cx, cy + 0.02 * h), Math.Min(irx * 0.9, irx * 0.13 * ps), iry * 0.95);
            else if (_look.PupilSize > 0)
                dc.DrawEllipse(_pupil, null, new Point(cx, cy + 0.04 * h), Math.Min(irx * 0.95, irx * _look.PupilSize * ps), Math.Min(iry * 0.95, iry * Math.Max(0.28, _look.PupilSize * 1.2) * ps));

            // the lashes shade the top of the eye (under the shine, so the shine stays white)
            double peakY = Bezier(A, U1, U2, B, 0.5).Y;
            dc.DrawRectangle(_shadow, null, new Rect(0, peakY - 0.05 * h, w, 0.42 * h));

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
            foreach (var (t, len) in new[] { (0.10, 0.17), (0.24, 0.20), (0.38, 0.19), (0.52, 0.15) })
            {
                var p = Bezier(A, U1, U2, B, t); var q = Bezier(A, U1, U2, B, t + 0.02);
                double dx = q.X - p.X, dy = q.Y - p.Y, l = Math.Max(0.001, Math.Sqrt(dx * dx + dy * dy));
                double nx = dy / l, ny = -dx / l;                        // the normal that points up
                double side = om * 2 - 1;                               // +1 open: lashes up … −1 shut: lashes droop down
                double ox = nx * side - 0.55, oy = ny * side;
                double ol = Math.Max(0.001, Math.Sqrt(ox * ox + oy * oy)); ox /= ol; oy /= ol;
                dc.DrawLine(lashPen, p, new Point(p.X + ox * h * len, p.Y + oy * h * len));
            }
        }

        dc.Pop();                      // rotation
        if (_mirror) dc.Pop();         // mirror
    }

    // ---- life: animations that run only while the face is on show ----

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

    /// <summary>Starts the aura's breathing, the sparkle's twinkle and (for slit eyes) the pupil widening.</summary>
    public void StartLife(double pulseSeconds)
    {
        if (_look.Aura is not null)
            BeginAnimation(PulseProperty, new DoubleAnimation(0.5, 1.0, TimeSpan.FromSeconds(pulseSeconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        if (_look.Sparkle)
            BeginAnimation(TwinkleProperty, new DoubleAnimation(0.3, 1.0, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        if (_look.Slit)
            BeginAnimation(PupilScaleProperty, new DoubleAnimation(1.0, 2.6, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
    }

    /// <summary>Stops everything and puts the eye back to a calm, open, centred state.</summary>
    public void StopLife()
    {
        foreach (var p in new[] { BlinkProperty, PulseProperty, TwinkleProperty, PupilScaleProperty, OpenProperty, SquintProperty, TiltProperty, GlowProperty })
            BeginAnimation(p, null);
        Blink = 0; Pulse = 1; Twinkle = 1; PupilScale = 1; Open = 1; Squint = 0; Tilt = 0; Glow = 1; GazeX = 0; GazeY = 0;
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
