using System.Windows;
using System.Windows.Threading;
using Nova.App.Integrations.Windows;
using Nova.Core.Configuration;
using Nova.Core.Data;

namespace Nova.App.Features.DynamicIsland;

/// <summary>
/// The "brain" behind the real anime eyes (<see cref="RealEye"/>): where they look (at the mouse pointer, or glancing around when it
/// is still), when they blink (random, sometimes twice, slower when sleepy), and what mood they are in (sleepy at low battery or late at
/// night, narrowed during a Pomodoro focus, wide-eyed for a notice, glowing with music). It only runs while the face is on show.
/// </summary>
public partial class IslandWindow
{
    readonly List<RealEye> _realEyes = new();
    readonly DispatcherTimer _eyeTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    double _gazeX, _gazeY, _gazeTX, _gazeTY, _blinkIn = 2.5, _wanderIn = 1.5, _still;
    int _lastMx = int.MinValue, _lastMy, _moodTicks;
    EyeMood _mood = EyeMood.Normal;

    /// <summary>The eye styles that are drawn as real eyes; null for the older shape-based styles.</summary>
    static EyeLook? RealLook(IslandFaceStyle style) => style switch
    {
        IslandFaceStyle.AnimeA => EyeLooks.AnimeA,
        IslandFaceStyle.AnimeB => EyeLooks.AnimeB,
        IslandFaceStyle.AnimeC => EyeLooks.AnimeC,
        IslandFaceStyle.AnimeAura => EyeLooks.AnimeAura,
        IslandFaceStyle.Yandere => EyeLooks.Yandere,
        IslandFaceStyle.Demon => EyeLooks.Demon,
        IslandFaceStyle.Hollow => EyeLooks.Hollow,
        _ => null
    };

    /// <summary>The chosen style, or (with "follow the time of day") a glowing aura in the evening and Hollow at night.</summary>
    IslandFaceStyle EffectiveFaceStyle()
    {
        var chosen = _s.IslandFaceStyle;
        if (!_s.IslandEyeAutoTheme) return chosen;
        double t = DateTime.Now.TimeOfDay.TotalHours;
        if (t >= 22 || t < 5) return IslandFaceStyle.Hollow;
        if (t >= 17.5) return IslandFaceStyle.AnimeAura;
        return chosen;
    }

    /// <summary>Called now and then while the face is shown: swaps the eyes when the time of day moved to another style.</summary>
    void RefreshEyeTheme()
    {
        if (_s.IslandFaceMode == IslandFaceMode.ClockOnly || EffectiveFaceStyle() == _faceBuilt) return;
        bool shown = FaceBox.Visibility == Visibility.Visible;
        BuildFace();
        if (shown) StartFaceAnimation();
    }

    void StartRealEyes()
    {
        if (_realEyes.Count == 0) return;
        _gazeX = _gazeY = _gazeTX = _gazeTY = 0;
        _blinkIn = 1.2 + _rng.NextDouble() * 2; _wanderIn = 1.5; _still = 0; _lastMx = int.MinValue;
        _mood = EyeMood.Normal; _moodTicks = 0; _batterySec = 999;
        UpdateEyeMood(force: true);
        _eyeTimer.Start();
    }

    void StopRealEyes()
    {
        _eyeTimer.Stop();
        foreach (var e in _realEyes) e.StopLife();
    }

    void EyeBrainTick()
    {
        if (_realEyes.Count == 0 || !_faceAnimating) { _eyeTimer.Stop(); return; }
        const double dt = 0.04;

        // 1. where to look: at the pointer while it moves, otherwise glance around
        bool followed = false;
        if (_s.IslandEyeFollowMouse && NativeMethods.TryGetCursor(out int mx, out int my))
        {
            if (Math.Abs(mx - _lastMx) + Math.Abs(my - _lastMy) > 3) { _still = 0; _lastMx = mx; _lastMy = my; }
            else _still += dt;
            if (_still < 6)
            {
                try
                {
                    var c = FaceBox.PointToScreen(new Point(FaceBox.ActualWidth / 2, FaceBox.ActualHeight / 2));
                    _gazeTX = Math.Max(-1, Math.Min(1, (mx - c.X) / 420));
                    _gazeTY = Math.Max(-1, Math.Min(1, (my - c.Y) / 260));
                    _wanderIn = 1;
                    followed = true;
                }
                catch (InvalidOperationException) { }   // not on screen right now
            }
        }
        if (!followed)
        {
            _wanderIn -= dt;
            if (_wanderIn <= 0)
            {
                _wanderIn = 1.6 + _rng.NextDouble() * 3;
                _gazeTX = (_rng.NextDouble() * 2 - 1) * 0.85;
                _gazeTY = (_rng.NextDouble() * 2 - 1) * 0.5;
                if (_rng.NextDouble() < 0.3) { _gazeTX = 0; _gazeTY = 0; }   // now and then look straight ahead
            }
        }
        _gazeX += (_gazeTX - _gazeX) * 0.30;
        _gazeY += (_gazeTY - _gazeY) * 0.30;
        foreach (var e in _realEyes)
        {
            if (Math.Abs(e.GazeX - _gazeX) > 0.004) e.GazeX = _gazeX;   // no redraw while the gaze is not changing
            if (Math.Abs(e.GazeY - _gazeY) > 0.004) e.GazeY = _gazeY;
        }

        // 2. blinking
        _blinkIn -= dt;
        if (_blinkIn <= 0)
        {
            bool slow = _mood == EyeMood.Sleepy;
            foreach (var e in _realEyes) e.BlinkOnce(slow);
            _blinkIn = (slow ? 1.8 : 2.4) + _rng.NextDouble() * (slow ? 3.0 : 4.5);
            if (_rng.NextDouble() < 0.18) _blinkIn = 0.30;   // a quick double blink
        }

        // 3. mood, once a second
        if (++_moodTicks >= 25) { _moodTicks = 0; UpdateEyeMood(force: false); }
    }

    int _batterySec = 999;
    bool _batteryLow;

    bool BatteryLow()
    {
        if (++_batterySec >= 30)   // asked once a second, read every 30 s
        {
            _batterySec = 0;
            try { var b = MetricsProvider?.Invoke().Battery; _batteryLow = b is { Percent: < 20, OnAc: false }; }
            catch { _batteryLow = false; }
        }
        return _batteryLow;
    }

    /// <summary>Chooses the mood from what is going on: notice (alert) > Pomodoro focus > music > low battery or small hours (sleepy).</summary>
    void UpdateEyeMood(bool force)
    {
        var m = EyeMood.Normal;
        if (_s.IslandEyeMoods)
        {
            if (_noticeActive) m = EyeMood.Alert;
            else if (_pomo.Running && _pomo.Phase == PomodoroPhase.Focus) m = EyeMood.Focus;
            else if (_s.ShowMedia && _media is { HasTrack: true, IsPlaying: true }) m = EyeMood.Music;
            else if (BatteryLow() || DateTime.Now.Hour < 5) m = EyeMood.Sleepy;
        }
        if (!force && m == _mood) return;
        _mood = m;
        double pulse = m == EyeMood.Music ? 0.5 : m == EyeMood.Alert ? 0.6 : 1.4;   // the aura breathes faster with music
        foreach (var e in _realEyes) { e.SetMood(m); e.StartLife(pulse); }
    }
}
