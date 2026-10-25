namespace Nova.Core.Data;

public enum PomodoroPhase { Focus, ShortBreak, LongBreak }

/// <summary>Focus / break timer. Pure logic: the caller passes the current time, so it needs no timer of its own and is easy to test.</summary>
public sealed class PomodoroTimer
{
    public const int FocusMinutes = 25, ShortBreakMinutes = 5, LongBreakMinutes = 15, FocusBeforeLongBreak = 4;

    TimeSpan _remaining = TimeSpan.FromMinutes(FocusMinutes);
    DateTime _endUtc;

    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Focus;
    public bool Running { get; private set; }
    /// <summary>Focus sessions finished since the last reset.</summary>
    public int CompletedFocus { get; private set; }

    public static TimeSpan LengthOf(PomodoroPhase p) => TimeSpan.FromMinutes(p switch
    {
        PomodoroPhase.Focus => FocusMinutes,
        PomodoroPhase.ShortBreak => ShortBreakMinutes,
        _ => LongBreakMinutes
    });

    public TimeSpan Remaining(DateTime nowUtc)
        => Running ? (_endUtc > nowUtc ? _endUtc - nowUtc : TimeSpan.Zero) : _remaining;

    /// <summary>0..1 of the current phase that is used up.</summary>
    public double Fraction(DateTime nowUtc)
    {
        var total = LengthOf(Phase).TotalSeconds;
        return Math.Min(1, Math.Max(0, 1 - Remaining(nowUtc).TotalSeconds / total));
    }

    public void Start(DateTime nowUtc)
    {
        if (Running) return;
        if (_remaining <= TimeSpan.Zero) _remaining = LengthOf(Phase);
        _endUtc = nowUtc + _remaining;
        Running = true;
    }

    public void Pause(DateTime nowUtc)
    {
        if (!Running) return;
        _remaining = Remaining(nowUtc);
        Running = false;
    }

    public void Toggle(DateTime nowUtc) { if (Running) Pause(nowUtc); else Start(nowUtc); }

    public void Reset()
    {
        Running = false;
        Phase = PomodoroPhase.Focus;
        CompletedFocus = 0;
        _remaining = LengthOf(Phase);
    }

    /// <summary>Moves to the next phase without counting it as finished (stopped).</summary>
    public void Skip() => Advance(count: false);

    /// <summary>Call regularly while running. Returns true when a phase just ended (the next phase is ready but stopped).</summary>
    public bool Tick(DateTime nowUtc)
    {
        if (!Running || nowUtc < _endUtc) return false;
        Advance(count: true);
        return true;
    }

    void Advance(bool count)
    {
        Running = false;
        if (Phase == PomodoroPhase.Focus)
        {
            if (count) CompletedFocus++;
            Phase = CompletedFocus > 0 && CompletedFocus % FocusBeforeLongBreak == 0 && count ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        }
        else Phase = PomodoroPhase.Focus;
        _remaining = LengthOf(Phase);
    }
}
