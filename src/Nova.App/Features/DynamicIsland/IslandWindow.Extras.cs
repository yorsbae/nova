using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nova.App.Integrations.Windows;
using Nova.Core.Configuration;
using Nova.Core.Data;
using Nova.Core.Reminders;
using Nova.Core.Status;

namespace Nova.App.Features.DynamicIsland;

/// <summary>
/// v2.6: the little face, the drink / prayer / alarm pages, and the CPU / RAM indicators.
/// The three new pages are built in code (like the clipboard rows) so every control gets an explicit accessible name.
/// </summary>
public partial class IslandWindow
{
    // ---- shared bits ----

    /// <summary>Set by the app: one reading of CPU, memory, disk, battery and network speed.</summary>
    public Func<SystemMetrics>? MetricsProvider { get; set; }

    /// <summary>Raised when a page changed a setting (for example the drink reminder switch): the app saves the settings.</summary>
    public event Action? SettingsEdited;

    readonly DispatcherTimer _faceTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _extraTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly DispatcherTimer _usageTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    void InitExtras()
    {
        _extraTimer.Tick += (_, _) => ExtraTick();
        _usageTimer.Tick += (_, _) => UsageTick();
    }

    TextBlock Lbl(string text, double size = 12, double opacity = 1, bool bold = false, TextAlignment align = TextAlignment.Left)
    {
        var t = new TextBlock { Text = text, FontSize = size, Opacity = opacity, TextWrapping = TextWrapping.Wrap, TextAlignment = align };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        t.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        return t;
    }

    Button TabBtn(string text, RoutedEventHandler click, string accessibleName, bool on = false)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("NovaTab"), Margin = new Thickness(2, 0, 2, 0) };
        if (on) b.Tag = "on";
        AutomationProperties.SetName(b, accessibleName);
        b.Click += click;
        return b;
    }

    TextBox MakeInput(string hint, int max, string accessibleName, KeyEventHandler? key = null)
    {
        var tb = new TextBox { Style = (Style)FindResource("NovaInput"), Tag = hint, MaxLength = max };
        AutomationProperties.SetName(tb, accessibleName);
        if (key is not null) tb.KeyDown += key;
        return tb;
    }

    static Brush Solid(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    /// <summary>One timer for alarms, adzan and drink reminders: it runs only when one of them is switched on, or while the Island is open.</summary>
    void UpdateExtraTimer()
    {
        bool needed = _data.Alarms.Any(a => a.Enabled) || _s.PrayerNotify || _s.HydrationEnabled || _expanded;
        if (needed && !_extraTimer.IsEnabled) { _extraTimer.Start(); if (IsLoaded) ExtraTick(); }
        else if (!needed) _extraTimer.Stop();
    }

    void ExtraTick()
    {
        var now = DateTime.Now;
        var rung = _data.TakeDueAlarms(now);
        if (rung.Count > 0)
        {
            SaveData();
            RingAlarms(rung);
            if (_expanded && _pageIndex == Calendar) RebuildAlarms();
        }
        CheckPrayer(now);
        CheckWater(now);
        if (_expanded && _pageIndex == PrayerPage) RefreshPrayer();
    }

    // ================= the face =================

    bool _faceVisible;
    int _faceSec;
    IslandFaceStyle? _faceBuilt;
    double _faceBuiltH;
    readonly List<ScaleTransform> _blinks = new();
    TranslateTransform? _look;
    TextBlock? _emoticon;
    bool _faceAnimating;
    static readonly string[] Emoticons = { "(•‿•)", "(^‿^)", "(◕‿◕)", "(¬‿¬)", "(•ᴗ•)", "(o_o)", "(^_^)", "(-_-)", "(>_<)" };

    void InitFace()
    {
        _faceTimer.Tick += (_, _) => FaceTick();
        FaceBox.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(_faceScale); group.Children.Add(_faceRot); group.Children.Add(_faceMove);
        FaceBox.RenderTransform = group;
        _moodTimer.Tick += (_, _) => MoodTick();
    }

    // Random little actions on top of blinking and looking around, so the face does not repeat itself.
    readonly DispatcherTimer _moodTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };
    readonly ScaleTransform _faceScale = new(1, 1);
    readonly RotateTransform _faceRot = new(0);
    readonly TranslateTransform _faceMove = new();
    readonly Random _rng = new();
    int _lastMood = -1;

    void MoodTick()
    {
        if (!_faceAnimating || ReducedMotion) return;
        int m; do { m = _rng.Next(8); } while (m == _lastMood);
        _lastMood = m;
        _moodTimer.Interval = TimeSpan.FromSeconds(1.4 + _rng.NextDouble() * 2.2);
        double dir = _rng.Next(2) == 0 ? 1 : -1;
        DoubleAnimationUsingKeyFrames Keys(params (double v, double t)[] k)
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(k[^1].t) };
            foreach (var (v, t) in k) a.KeyFrames.Add(new EasingDoubleKeyFrame(v, TimeSpan.FromSeconds(t), new SineEase()));
            return a;
        }
        switch (m)
        {
            case 0: _faceMove.BeginAnimation(TranslateTransform.YProperty, Keys((-4, 0.18), (0, 0.36), (-2, 0.5), (0, 0.64))); break;       // hop
            case 1: _faceRot.BeginAnimation(RotateTransform.AngleProperty, Keys((12 * dir, 0.3), (12 * dir, 0.9), (0, 1.2))); break;          // tilt head
            case 2: _faceScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((1.22, 0.2), (1, 0.45)));
                    _faceScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((1.22, 0.2), (1, 0.45))); break;                           // pop
            case 3: _faceRot.BeginAnimation(RotateTransform.AngleProperty, Keys((-8, 0.1), (8, 0.2), (-6, 0.3), (6, 0.4), (0, 0.5))); break;  // shake
            case 4: _faceMove.BeginAnimation(TranslateTransform.YProperty, Keys((3, 0.2), (0, 0.4), (3, 0.6), (0, 0.8))); break;              // nod
            case 5: _faceScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((1.25, 0.18), (1, 0.4)));
                    _faceScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0.78, 0.18), (1, 0.4))); break;                           // squish
            case 6: _faceRot.BeginAnimation(RotateTransform.AngleProperty, Keys((360 * dir, 0.8))); break;                                    // spin
            default:
                _faceMove.BeginAnimation(TranslateTransform.XProperty, Keys((4 * dir, 0.3), (-4 * dir, 0.7), (0, 1.0)));                        // sway
                break;
        }
        if (_emoticon is not null) _emoticon.Text = Emoticons[_rng.Next(Emoticons.Length)];
    }

    /// <summary>Called whenever settings change: (re)builds the face and starts or stops the clock-and-face rhythm.</summary>
    void ApplyFace()
    {
        if (_s.IslandFaceMode == IslandFaceMode.ClockOnly)
        {
            _faceTimer.Stop();
            _faceVisible = false;
            ShowFace(false);
            return;
        }
        BuildFace();
        _faceSec = 0; _slot = 0; _slotSec = 0; _moduleText = null;
        _faceVisible = _s.IslandFaceMode == IslandFaceMode.FaceOnly;
        if (!_faceTimer.IsEnabled) _faceTimer.Start();
    }

    void FaceTick()
    {
        _faceSec++;
        bool was = _faceVisible;
        string? wasModule = _moduleText;
        if (_s.IslandFaceMode == IslandFaceMode.FaceOnly) { _faceVisible = true; _moduleText = null; }
        else if (_s.IslandFaceMode == IslandFaceMode.AllModules)
        {
            // Minimal mode "all modules": clock → face → water → prayer, each for a few seconds (only the ones that are switched on).
            int clock = Math.Max(5, _s.IslandFaceClockSeconds), show = Math.Max(3, _s.IslandFaceShowSeconds);
            if (++_slotSec >= (_slot == 0 ? clock : show)) { _slotSec = 0; _slot = NextSlot(_slot); }
            _faceVisible = _slot == 1;
            _moduleText = _slot == 2 ? WaterCompactText() : _slot == 3 ? PrayerCompactText() : null;
            if (_moduleText is null && _slot >= 2) { _slot = 0; _slotSec = 0; }
        }
        else
        {
            int clock = Math.Max(5, _s.IslandFaceClockSeconds), show = Math.Max(3, _s.IslandFaceShowSeconds);
            _faceVisible = _faceSec % (clock + show) >= clock;
        }
        if (_faceVisible && _emoticon is not null && _faceSec % 4 == 0)
            _emoticon.Text = Emoticons[(_faceSec / 4) % Emoticons.Length];
        if (was != _faceVisible || wasModule != _moduleText) UpdateCompactText();
    }

    int _slot, _slotSec;
    string? _moduleText;

    /// <summary>Next compact slot after <paramref name="slot"/>: 0 clock, 1 face, 2 water (if reminders are on), 3 next prayer.</summary>
    int NextSlot(int slot)
    {
        for (int i = 1; i <= 4; i++)
        {
            int s = (slot + i) % 4;
            if (s == 2 && !_s.HydrationEnabled) continue;
            return s;
        }
        return 0;
    }

    string? WaterCompactText()
    {
        if (!_s.HydrationEnabled) return null;
        return $"💧 {_data.WaterOn(DateTime.Today)}/{_s.HydrationGoalGlasses} gelas";
    }

    string? PrayerCompactText()
    {
        try
        {
            var (_, lat, lon) = PrayerSpot();
            var now = DateTime.Now;
            var next = PrayerCalculator.Next(now, lat, lon, UtcOffset);
            if (next is null) return null;
            var left = next.Time - now;
            string span = left.TotalHours >= 1 ? $"{(int)left.TotalHours}:{left.Minutes:00}" : $"{Math.Max(0, left.Minutes)} m";
            return $"🕌 {next.Name} {span}";
        }
        catch { return null; }
    }

    void ShowFace(bool show)
    {
        if (FaceBox.Child is null) show = false;
        TextCol.Visibility = show ? Visibility.Hidden : Visibility.Visible;   // hidden, not collapsed: the Island keeps its size
        FaceBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) StartFaceAnimation(); else StopFaceAnimation();
    }

    Border Blob(double w, double h, double radius, string? resourceKey, Brush? brush = null)
    {
        var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(radius) };
        if (resourceKey is not null) b.SetResourceReference(Border.BackgroundProperty, resourceKey);
        else b.Background = brush;
        return b;
    }

    Grid MakeEye(IslandFaceStyle style, double h)
    {
        var g = new Grid { RenderTransformOrigin = new Point(0.5, 0.5), Margin = new Thickness(h * 0.18, 0, h * 0.18, 0), VerticalAlignment = VerticalAlignment.Center };
        switch (style)
        {
            case IslandFaceStyle.Robot:
            {
                var glow = Blob(h * 0.72, h * 0.46, h * 0.14, "NovaAccent");
                glow.Opacity = 0.28;
                g.Children.Add(glow);
                g.Children.Add(Blob(h * 0.54, h * 0.30, h * 0.08, "NovaAccent"));
                break;
            }
            case IslandFaceStyle.Anime:
            {
                g.Children.Add(Blob(h * 0.58, h * 0.82, h * 0.29, "NovaAccent"));
                var pupil = Blob(h * 0.30, h * 0.48, h * 0.15, null, Brushes.Black);
                pupil.VerticalAlignment = VerticalAlignment.Bottom; pupil.Margin = new Thickness(0, 0, 0, h * 0.10);
                g.Children.Add(pupil);
                var big = Blob(h * 0.17, h * 0.17, h * 0.085, null, Brushes.White);
                big.HorizontalAlignment = HorizontalAlignment.Left; big.VerticalAlignment = VerticalAlignment.Top; big.Margin = new Thickness(h * 0.10, h * 0.12, 0, 0);
                g.Children.Add(big);
                var small = Blob(h * 0.09, h * 0.09, h * 0.045, null, Brushes.White);
                small.HorizontalAlignment = HorizontalAlignment.Right; small.VerticalAlignment = VerticalAlignment.Bottom; small.Margin = new Thickness(0, 0, h * 0.10, h * 0.18);
                g.Children.Add(small);
                break;
            }
            case IslandFaceStyle.Cat:
            {
                g.Children.Add(Blob(h * 0.58, h * 0.58, h * 0.29, null, Solid("#FFFFD34D")));
                g.Children.Add(Blob(h * 0.12, h * 0.50, h * 0.06, null, Brushes.Black));
                break;
            }
            default:   // Pet
            {
                g.Children.Add(Blob(h * 0.40, h * 0.60, h * 0.20, "NovaOnSurface"));
                var spark = Blob(h * 0.15, h * 0.15, h * 0.075, "NovaSurface");
                spark.HorizontalAlignment = HorizontalAlignment.Left; spark.VerticalAlignment = VerticalAlignment.Top; spark.Margin = new Thickness(h * 0.07, h * 0.10, 0, 0);
                g.Children.Add(spark);
                break;
            }
        }
        return g;
    }

    /// <summary>A closed eye: a small downward arc, for the sleepy face.</summary>
    FrameworkElement MakeSleepyEye(double h)
    {
        double w = h * 0.5;
        var figure = new PathFigure { StartPoint = new Point(0, 0), IsClosed = false };
        figure.Segments.Add(new QuadraticBezierSegment(new Point(w / 2, h * 0.42), new Point(w, 0), true));
        var arc = new Path
        {
            Data = new PathGeometry(new[] { figure }), StrokeThickness = Math.Max(2, h * 0.10),
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(h * 0.16, 0, h * 0.16, 0), VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5)
        };
        arc.SetResourceReference(Shape.StrokeProperty, "NovaOnSurface");
        return arc;
    }

    void BuildFace()
    {
        var style = _s.IslandFaceStyle;
        double h = Math.Max(20, _s.ClockFontSize * 1.2);
        if (_faceBuilt == style && Math.Abs(_faceBuiltH - h) < 0.5 && FaceBox.Child is not null) return;
        StopFaceAnimation();
        _faceBuilt = style; _faceBuiltH = h;
        _blinks.Clear(); _emoticon = null;

        if (style == IslandFaceStyle.Emoticon)
        {
            _emoticon = new TextBlock { Text = Emoticons[0], FontSize = Math.Max(14, h * 0.78), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            _emoticon.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
            FaceBox.Child = _emoticon;
            return;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = h, HorizontalAlignment = HorizontalAlignment.Center };
        _look = new TranslateTransform();
        row.RenderTransform = _look;
        for (int i = 0; i < 2; i++)
        {
            FrameworkElement eye = style == IslandFaceStyle.Sleepy ? MakeSleepyEye(h) : MakeEye(style, h);
            var scale = new ScaleTransform(1, 1);
            eye.RenderTransform = scale;
            _blinks.Add(scale);
            row.Children.Add(eye);
        }
        if (style == IslandFaceStyle.Sleepy)
        {
            var z = Lbl("z", Math.Max(10, h * 0.45), 0.6, bold: true);
            z.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(z);
        }
        FaceBox.Child = row;
    }

    void StartFaceAnimation()
    {
        if (_faceAnimating || ReducedMotion || _blinks.Count == 0) return;
        _faceAnimating = true;
        _moodTimer.Start();
        bool sleepy = _faceBuilt == IslandFaceStyle.Sleepy;
        foreach (var scale in _blinks)
        {
            if (sleepy)
            {
                // Slow breathing instead of blinking.
                var breathe = new DoubleAnimation(1, 0.7, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
                continue;
            }
            var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4.4), RepeatBehavior = RepeatBehavior.Forever };
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromSeconds(3.0)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.08, TimeSpan.FromSeconds(3.12)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromSeconds(3.26)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromSeconds(3.7)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.08, TimeSpan.FromSeconds(3.8)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromSeconds(3.92)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromSeconds(4.4)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
        }
        if (_look is not null && !sleepy)
        {
            double d = _faceBuiltH * 0.16;
            var look = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(7.4), RepeatBehavior = RepeatBehavior.Forever };
            look.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromSeconds(1.2)));
            look.KeyFrames.Add(new EasingDoubleKeyFrame(d, TimeSpan.FromSeconds(1.6), new QuadraticEase()));
            look.KeyFrames.Add(new EasingDoubleKeyFrame(d, TimeSpan.FromSeconds(2.6)));
            look.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromSeconds(3.0), new QuadraticEase()));
            look.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromSeconds(4.4)));
            look.KeyFrames.Add(new EasingDoubleKeyFrame(-d, TimeSpan.FromSeconds(4.8), new QuadraticEase()));
            look.KeyFrames.Add(new EasingDoubleKeyFrame(-d, TimeSpan.FromSeconds(5.8)));
            look.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromSeconds(6.2), new QuadraticEase()));
            look.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromSeconds(7.4)));
            _look.BeginAnimation(TranslateTransform.XProperty, look);
        }
    }

    void StopFaceAnimation()
    {
        if (!_faceAnimating) return;
        _faceAnimating = false;
        _moodTimer.Stop();
        _faceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); _faceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _faceRot.BeginAnimation(RotateTransform.AngleProperty, null);
        _faceMove.BeginAnimation(TranslateTransform.XProperty, null); _faceMove.BeginAnimation(TranslateTransform.YProperty, null);
        _faceScale.ScaleX = _faceScale.ScaleY = 1; _faceRot.Angle = 0; _faceMove.X = _faceMove.Y = 0;
        foreach (var scale in _blinks) { scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleY = 1; }
        if (_look is not null) { _look.BeginAnimation(TranslateTransform.XProperty, null); _look.X = 0; }
    }

    // ================= CPU / RAM indicators =================

    string _usageText = "";
    StackPanel? _usagePanel;
    TextBlock _netText = null!;
    readonly Dictionary<string, (ProgressBar Bar, TextBlock Value, FrameworkElement Row)> _usage = new();

    /// <summary>The small "CPU 12% · RAM 48%" line in the compact Island is read every 3 s, and only while it is shown.</summary>
    void UpdateUsageTimer()
    {
        bool needed = _s.IslandShowUsage && !_expanded && MetricsProvider is not null && IsVisible;
        if (needed && !_usageTimer.IsEnabled) { _usageTimer.Start(); UsageTick(); }
        else if (!needed)
        {
            _usageTimer.Stop();
            if (_usageText.Length > 0) { _usageText = ""; UpdateCompactText(); }
        }
    }

    void UsageTick()
    {
        if (MetricsProvider is null) return;
        try
        {
            var m = MetricsProvider();
            var text = RateFormatter.Compact(m.Cpu, m.Memory);
            if (text != _usageText) { _usageText = text; if (!_expanded) UpdateCompactText(); }
        }
        catch { /* a reading that fails is skipped */ }
    }

    void BuildUsagePanel()
    {
        _usagePanel = new StackPanel { Margin = new Thickness(6, 0, 6, 8) };
        foreach (var (key, label) in new[] { ("cpu", "CPU"), ("mem", "RAM"), ("disk", "Disk"), ("bat", "Baterai") })
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            var name = Lbl(label, 12, 0.8);
            var bar = new ProgressBar { Style = (Style)FindResource("NovaThinProgress"), Height = 6, VerticalAlignment = VerticalAlignment.Center };
            var value = Lbl("", 12, 1, align: TextAlignment.Right);
            Grid.SetColumn(bar, 1); Grid.SetColumn(value, 2);
            grid.Children.Add(name); grid.Children.Add(bar); grid.Children.Add(value);
            AutomationProperties.SetName(grid, label);
            _usagePanel.Children.Add(grid);
            _usage[key] = (bar, value, grid);
        }
        _netText = Lbl("", 12, 0.8, align: TextAlignment.Left);
        _netText.Margin = new Thickness(0, 4, 0, 0);
        AutomationProperties.SetName(_netText, "Kecepatan jaringan");
        _usagePanel.Children.Add(_netText);
        PageSystem.Children.Insert(0, _usagePanel);
    }

    void RefreshUsage()
    {
        if (_usagePanel is null || !_expanded || _pageIndex != SystemPage || MetricsProvider is null) return;
        SystemMetrics m;
        try { m = MetricsProvider(); } catch { return; }
        SetUsage("cpu", m.Cpu);
        SetUsage("mem", m.Memory);
        SetUsage("disk", m.Disk);
        SetUsage("bat", m.Battery?.Percent, lowIsBad: true, suffix: m.Battery is { Charging: true } ? " ⚡" : "");
        _netText.Text = $"↓ {RateFormatter.Format(m.DownBytesPerSecond)}    ↑ {RateFormatter.Format(m.UpBytesPerSecond)}";
    }

    void SetUsage(string key, double? value, bool lowIsBad = false, string suffix = "")
    {
        var (bar, text, row) = _usage[key];
        row.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        if (value is null) return;
        bar.Value = Math.Clamp(value.Value / 100.0, 0, 1);
        text.Text = value.Value.ToString("0", CultureInfo.CurrentCulture) + "%" + suffix;
        bool hot = lowIsBad ? value.Value < 20 : value.Value >= 85;
        if (hot) bar.Foreground = Solid("#FFE5484D"); else bar.SetResourceReference(Control.ForegroundProperty, "NovaAccent");
    }

    // ================= drink reminder =================

    TextBlock _waterCount = null!, _waterSub = null!;
    WrapPanel _waterDrops = null!;
    ProgressBar _waterBar = null!;
    Button _waterToggle = null!;
    readonly List<Button> _waterIntervals = new();
    DateTime? _waterNext;
    static readonly int[] WaterIntervals = { 30, 45, 60, 90, 120 };

    UIElement BuildWaterPage()
    {
        var page = new StackPanel();
        _waterCount = Lbl("", 26, 1, bold: true, align: TextAlignment.Center);
        AutomationProperties.SetName(_waterCount, "Jumlah gelas air hari ini");
        _waterBar = new ProgressBar { Style = (Style)FindResource("NovaThinProgress"), Margin = new Thickness(24, 6, 24, 8) };
        AutomationProperties.SetName(_waterBar, "Progres minum hari ini");
        _waterDrops = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        _waterSub = Lbl("", 11, 0.7, align: TextAlignment.Center);
        _waterSub.Margin = new Thickness(0, 8, 0, 6);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(TabBtn("+ 1 gelas", (_, _) => AddWater(1), "Tambah satu gelas"));
        buttons.Children.Add(TabBtn("− 1", (_, _) => AddWater(-1), "Kurangi satu gelas"));
        _waterToggle = TabBtn("", OnWaterToggle, "Nyalakan atau matikan pengingat minum");
        buttons.Children.Add(_waterToggle);

        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        chips.Children.Add(Lbl("Ingatkan tiap", 11, 0.7));
        foreach (var minutes in WaterIntervals)
        {
            int m = minutes;
            var chip = TabBtn(m + " mnt", (_, _) => SetWaterInterval(m), $"Ingatkan setiap {m} menit");
            _waterIntervals.Add(chip);
            chips.Children.Add(chip);
        }
        foreach (var child in new UIElement[] { _waterCount, _waterBar, _waterDrops, _waterSub, buttons, chips }) page.Children.Add(child);
        return page;
    }

    void RefreshWater()
    {
        int goal = _s.HydrationGoalGlasses, n = _data.WaterOn(DateTime.Today);
        _waterCount.Text = $"{n} / {goal} gelas";
        _waterBar.Value = Math.Clamp((double)n / goal, 0, 1);
        _waterDrops.Children.Clear();
        for (int i = 0; i < Math.Min(Math.Max(goal, n), 24); i++)
        {
            var drop = new Ellipse { Width = 14, Height = 14, Margin = new Thickness(3), StrokeThickness = 1.5 };
            if (i < n) { drop.Fill = Solid("#FF3DA5FF"); drop.Stroke = Solid("#FF3DA5FF"); }
            else { drop.Fill = Brushes.Transparent; drop.SetResourceReference(Shape.StrokeProperty, "NovaBorder"); }
            _waterDrops.Children.Add(drop);
        }
        _waterToggle.Content = _s.HydrationEnabled ? "Pengingat: Aktif" : "Pengingat: Mati";
        _waterToggle.Tag = _s.HydrationEnabled ? "on" : null;
        for (int i = 0; i < _waterIntervals.Count; i++) _waterIntervals[i].Tag = WaterIntervals[i] == _s.HydrationIntervalMinutes ? "on" : null;
        _waterSub.Text = !_s.HydrationEnabled
            ? "Pengingat mati. Nyalakan agar NOVA mengingatkan Anda minum."
            : $"Pengingat tiap {_s.HydrationIntervalMinutes} menit, {_s.HydrationFromHour:00}.00–{_s.HydrationToHour:00}.00" +
              (_waterNext is { } next ? $" · berikutnya {next:HH:mm}" : "");
    }

    void AddWater(int delta)
    {
        _data.AddWater(DateTime.Today, delta);
        SaveData();
        if (delta > 0 && _s.HydrationEnabled) _waterNext = HydrationSchedule.NextAfter(DateTime.Now, _s.HydrationIntervalMinutes, _s.HydrationFromHour, _s.HydrationToHour);
        RefreshWater();
    }

    void OnWaterToggle(object sender, RoutedEventArgs e)
    {
        _s.HydrationEnabled = !_s.HydrationEnabled;
        _waterNext = null;
        if (_s.HydrationEnabled) CheckWater(DateTime.Now);   // sets the first reminder time
        UpdateExtraTimer();
        SettingsEdited?.Invoke();
        RefreshWater();
    }

    void SetWaterInterval(int minutes)
    {
        _s.HydrationIntervalMinutes = minutes;
        _waterNext = _s.HydrationEnabled ? HydrationSchedule.NextAfter(DateTime.Now, minutes, _s.HydrationFromHour, _s.HydrationToHour) : null;
        SettingsEdited?.Invoke();
        RefreshWater();
    }

    void CheckWater(DateTime now)
    {
        if (!_s.HydrationEnabled) { _waterNext = null; return; }
        _waterNext ??= HydrationSchedule.NextAfter(now, _s.HydrationIntervalMinutes, _s.HydrationFromHour, _s.HydrationToHour);
        if (now < _waterNext) return;
        _waterNext = HydrationSchedule.NextAfter(now, _s.HydrationIntervalMinutes, _s.HydrationFromHour, _s.HydrationToHour);
        int n = _data.WaterOn(now.Date);
        if (n >= _s.HydrationGoalGlasses || !HydrationSchedule.InWindow(now, _s.HydrationFromHour, _s.HydrationToHour)) return;
        ShowNotice($"💧 Waktunya minum · {n}/{_s.HydrationGoalGlasses} gelas hari ini", important: true, seconds: 12);
        AlertSound.Play(null, loop: false, maxSeconds: 3);
        if (_expanded && _pageIndex == Water) RefreshWater();
    }

    // ================= prayer times =================

    TextBlock _prayerHead = null!, _prayerNext = null!;
    StackPanel _prayerList = null!;
    Button _prayerNotifyBtn = null!, _prayerSoundBtn = null!;
    readonly HashSet<string> _announced = new();

    (string Name, double Lat, double Lon) PrayerSpot()
    {
        if (_s.PrayerCity == PrayerPlaces.Custom) return ("Koordinat sendiri", _s.PrayerLatitude, _s.PrayerLongitude);
        return PrayerPlaces.Find(_s.PrayerCity) is { } p ? (p.Name, p.Latitude, p.Longitude) : ("Jakarta", -6.2088, 106.8456);
    }

    static double UtcOffset(DateTime day) => TimeZoneInfo.Local.GetUtcOffset(day).TotalHours;

    UIElement BuildPrayerPage()
    {
        var page = new StackPanel();
        _prayerHead = Lbl("", 12, 0.8, align: TextAlignment.Center);
        _prayerNext = Lbl("", 15, 1, bold: true, align: TextAlignment.Center);
        _prayerNext.Margin = new Thickness(0, 2, 0, 6);
        _prayerNext.SetResourceReference(TextBlock.ForegroundProperty, "NovaAccent");
        AutomationProperties.SetName(_prayerNext, "Waktu sholat berikutnya");
        _prayerList = new StackPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        _prayerNotifyBtn = TabBtn("", OnPrayerNotify, "Nyalakan atau matikan pemberitahuan adzan");
        _prayerSoundBtn = TabBtn("", OnPrayerSound, "Nyalakan atau matikan suara adzan");
        buttons.Children.Add(_prayerNotifyBtn); buttons.Children.Add(_prayerSoundBtn);
        var tip = Lbl("Dihitung di PC ini (tanpa internet), metode Kemenag. Ganti kota di Settings → Waktu sholat.", 10, 0.55, align: TextAlignment.Center);
        tip.Margin = new Thickness(0, 6, 0, 0);
        foreach (var child in new UIElement[] { _prayerHead, _prayerNext, _prayerList, buttons, tip }) page.Children.Add(child);
        return page;
    }

    void RefreshPrayer()
    {
        var (place, lat, lon) = PrayerSpot();
        var now = DateTime.Now;
        var today = PrayerCalculator.Compute(now.Date, lat, lon, UtcOffset(now.Date));
        _prayerHead.Text = $"{place} · {now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture)}";

        var next = PrayerCalculator.Next(now, lat, lon, UtcOffset);
        if (next is null) _prayerNext.Text = "Waktu sholat tidak bisa dihitung untuk lokasi ini.";
        else
        {
            var left = next.Time - now;
            string span = left.TotalHours >= 1 ? $"{(int)left.TotalHours} j {left.Minutes} m" : $"{Math.Max(0, left.Minutes)} m";
            _prayerNext.Text = $"{next.Name} {next.Time:HH:mm} · dalam {span}";
        }

        _prayerList.Children.Clear();
        foreach (var e in today)
        {
            bool isNext = next is not null && next.Prayer == e.Prayer && next.Time.Date == e.Time.Date;
            var row = BuildRow(e.Name, e.Time.ToString("HH:mm", CultureInfo.CurrentCulture), accent: isNext);
            if (e.Time <= now) row.Opacity = 0.5;
            if (!e.HasAdzan) row.Opacity = Math.Min(row.Opacity, 0.7);
            _prayerList.Children.Add(row);
        }
        _prayerNotifyBtn.Content = _s.PrayerNotify ? "Adzan: Aktif" : "Adzan: Mati";
        _prayerNotifyBtn.Tag = _s.PrayerNotify ? "on" : null;
        _prayerSoundBtn.Content = _s.PrayerSound ? "Suara: Ya" : "Suara: Tidak";
        _prayerSoundBtn.Tag = _s.PrayerSound ? "on" : null;
    }

    void OnPrayerNotify(object sender, RoutedEventArgs e)
    {
        _s.PrayerNotify = !_s.PrayerNotify;
        UpdateExtraTimer();
        SettingsEdited?.Invoke();
        RefreshPrayer();
    }

    void OnPrayerSound(object sender, RoutedEventArgs e)
    {
        _s.PrayerSound = !_s.PrayerSound;
        SettingsEdited?.Invoke();
        RefreshPrayer();
    }

    void CheckPrayer(DateTime now)
    {
        if (!_s.PrayerNotify) return;
        var (_, lat, lon) = PrayerSpot();
        var prefix = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        _announced.RemoveWhere(k => !k.StartsWith(prefix, StringComparison.Ordinal));
        foreach (var e in PrayerCalculator.Compute(now.Date, lat, lon, UtcOffset(now.Date)))
        {
            if (!e.HasAdzan || now < e.Time || now > e.Time.AddMinutes(3)) continue;
            if (!_announced.Add(prefix + e.Prayer)) continue;
            if (_s.PrayerSound) AlertSound.Play(_s.PrayerSoundPath, loop: false, maxSeconds: 400);
            ShowNotice($"🕌 Waktu {e.Name} · {e.Time:HH:mm}", important: true, seconds: 25, onClick: AlertSound.Stop);
        }
    }

    // ================= alarms =================

    StackPanel _alarmList = null!;
    TextBox _alarmTime = null!, _alarmLabel = null!;
    readonly Button[] _alarmDays = new Button[7];
    static readonly int[] DayOrder = { 1, 2, 3, 4, 5, 6, 0 };   // Monday first; the number is DayOfWeek
    static readonly string[] DayInitials = { "S", "S", "R", "K", "J", "S", "M" };
    static readonly string[] DayNames = { "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu", "Minggu" };

    UIElement BuildAlarmPage()
    {
        var page = new StackPanel();
        _alarmList = new StackPanel();
        page.Children.Add(new ScrollViewer { MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = _alarmList });

        var form = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _alarmTime = MakeInput("06:30", 5, "Jam alarm", OnAlarmKey);
        _alarmLabel = MakeInput("Nama alarm (boleh kosong)", 40, "Nama alarm", OnAlarmKey);
        _alarmLabel.Margin = new Thickness(6, 0, 0, 0);
        var add = new Button { Style = (Style)FindResource("NovaIconButton"), Width = 32, Height = 32, Margin = new Thickness(6, 0, 0, 0), Content = "\uE710", ToolTip = "Tambah alarm" };
        AutomationProperties.SetName(add, "Tambah alarm");
        add.Click += OnAlarmAdd;
        Grid.SetColumn(_alarmLabel, 1); Grid.SetColumn(add, 2);
        form.Children.Add(_alarmTime); form.Children.Add(_alarmLabel); form.Children.Add(add);
        page.Children.Add(form);

        var days = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 0; i < 7; i++)
        {
            var btn = TabBtn(DayInitials[i], (s, _) => { var b = (Button)s; b.Tag = b.Tag as string == "on" ? null : "on"; }, "Ulangi setiap hari " + DayNames[i]);
            btn.ToolTip = DayNames[i];
            _alarmDays[i] = btn;
            days.Children.Add(btn);
        }
        page.Children.Add(days);
        var hint = Lbl("Pilih hari untuk alarm berulang. Tanpa hari = berbunyi sekali saja. Klik pemberitahuan alarm untuk mematikan suaranya.", 10, 0.55, align: TextAlignment.Center);
        hint.Margin = new Thickness(0, 6, 0, 0);
        page.Children.Add(hint);
        return page;
    }

    /// <summary>Alarms are part of the calendar agenda now.</summary>
    void RebuildAlarms() => RebuildAgenda();

    void OnAlarmKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OnAlarmAdd(sender, e); e.Handled = true; } }

    void OnAlarmAdd(object sender, RoutedEventArgs e)
    {
        int mask = 0;
        for (int i = 0; i < 7; i++) if (_alarmDays[i].Tag as string == "on") mask |= 1 << DayOrder[i];
        if (!_data.AddAlarm(string.IsNullOrWhiteSpace(_alarmTime.Text) ? "06:00" : _alarmTime.Text, _alarmLabel.Text, mask))
        {
            ShowNotice(_data.Alarms.Count >= UserData.MaxAlarms ? "Alarm sudah penuh." : "Jam tidak dikenali. Contoh: 06:30", important: true);
            return;
        }
        _alarmTime.Clear(); _alarmLabel.Clear();
        SaveData();
        UpdateExtraTimer();
        RebuildAlarms();
    }

    void RingAlarms(List<AlarmItem> rung)
    {
        var first = rung[0];
        var text = "⏰ " + (first.Label.Length > 0 ? first.Label : "Alarm") + " · " + first.Time + (rung.Count > 1 ? $" (+{rung.Count - 1})" : "");
        AlertSound.Play(_s.AlarmSoundPath, loop: true, maxSeconds: 30);
        ShowNotice(text, important: true, seconds: 30, onClick: AlertSound.Stop);
    }
}
