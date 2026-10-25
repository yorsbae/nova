namespace Nova.Core.Media;

/// <summary>
/// Playback position as last reported by the media session. Windows only reports it now and then,
/// so the current position is extrapolated while the track is playing.
/// </summary>
public sealed record MediaTimeline(TimeSpan Position, TimeSpan Duration, DateTime ReportedAtUtc, bool IsPlaying)
{
    public TimeSpan Now(DateTime utcNow)
    {
        var p = Position;
        if (IsPlaying && utcNow > ReportedAtUtc) p += utcNow - ReportedAtUtc;
        if (p < TimeSpan.Zero) p = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
        return p;
    }

    /// <summary>0..1 progress, or 0 when the length is unknown (live streams).</summary>
    public double Fraction(DateTime utcNow)
        => Duration <= TimeSpan.Zero ? 0 : Math.Min(1, Math.Max(0, Now(utcNow).TotalSeconds / Duration.TotalSeconds));
}
