namespace Nova.Core.Media;

public sealed record MediaInfo(string Title, string Artist, bool IsPlaying, bool CanPlayPause, bool CanNext, bool CanPrevious)
{
    public bool HasTrack => !string.IsNullOrWhiteSpace(Title);

    public string Describe()
    {
        if (!HasTrack) return "";
        return string.IsNullOrWhiteSpace(Artist) ? Title.Trim() : $"{Title.Trim()} — {Artist.Trim()}";
    }

    /// <summary>True when a different track should be announced.</summary>
    public bool IsDifferentTrack(MediaInfo? other) => other is null || Title != other.Title || Artist != other.Artist;
}
