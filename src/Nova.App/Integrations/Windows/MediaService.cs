using System.IO;
using System.Windows.Threading;
using Nova.Core.Logging;
using Nova.Core.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Reads and controls whatever app currently owns the Windows media session (browser, Spotify, etc.) through the
/// supported GlobalSystemMediaTransportControls API: track, album art and playback position. Event-driven — no polling.
/// Windows can hold several sessions at once (a browser, Spotify, a paused video tab...) and its own "current" session is
/// not always the one that is making sound, so the session that is actually playing is preferred.
/// </summary>
public sealed class MediaService : IDisposable
{
    const long MaxArtBytes = 4 * 1024 * 1024;

    readonly Dispatcher _ui;
    readonly object _gate = new();
    readonly List<GlobalSystemMediaTransportControlsSession> _watched = new();
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    string _artKey = "";
    int _generation;
    bool _disposed;

    public event Action<MediaInfo?>? Changed;
    /// <summary>Album art (encoded image bytes) of the current track, or null if there is none.</summary>
    public event Action<byte[]?>? ArtChanged;
    public event Action<MediaTimeline?>? TimelineChanged;

    public MediaTimeline? Timeline { get; private set; }
    /// <summary>Windows app id of the player ("Spotify.exe", "chrome.exe"...), or null.</summary>
    public string? SourceApp { get; private set; }

    public MediaService(Dispatcher ui) => _ui = ui;

    public async Task StartAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed) return;
            _manager = manager;
            manager.CurrentSessionChanged += OnCurrentChanged;
            manager.SessionsChanged += OnSessionsChanged;
            Reselect(rewatch: true);
        }
        catch (Exception ex)
        {
            FileLog.Write($"Media session unavailable: {ex.GetType().Name}: {ex.Message}");
            Publish(null, null, 0);   // feature degrades; the rest of NOVA keeps working
        }
    }

    void OnCurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Reselect(rewatch: true);
    void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => Reselect(rewatch: true);

    void OnProperties(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    { if (IsCurrent(sender)) _ = RefreshAsync(true); }

    void OnTimeline(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    { if (IsCurrent(sender)) _ = RefreshAsync(false); }

    void OnPlayback(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        // Another player may have started playing while this one was the current one: pick again.
        if (Reselect(rewatch: false)) return;
        if (IsCurrent(sender)) _ = RefreshAsync(false);
    }

    static bool SameApp(GlobalSystemMediaTransportControlsSession a, GlobalSystemMediaTransportControlsSession b)
    {
        try { return string.Equals(a.SourceAppUserModelId, b.SourceAppUserModelId, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    bool IsCurrent(GlobalSystemMediaTransportControlsSession s)
    {
        lock (_gate) return _session is { } c && SameApp(c, s);
    }

    static bool IsPlaying(GlobalSystemMediaTransportControlsSession s)
    {
        try { return s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch { return false; }
    }

    /// <summary>The session that is playing (Windows' own choice first if it is playing), else Windows' choice, else any session.</summary>
    static GlobalSystemMediaTransportControlsSession? Pick(IReadOnlyList<GlobalSystemMediaTransportControlsSession> all, GlobalSystemMediaTransportControlsSession? current)
    {
        GlobalSystemMediaTransportControlsSession? playing = null;
        foreach (var s in all)
        {
            if (!IsPlaying(s)) continue;
            if (current is not null && SameApp(s, current)) return s;
            playing ??= s;
        }
        return playing ?? current ?? (all.Count > 0 ? all[0] : null);
    }

    void Watch(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            s.MediaPropertiesChanged += OnProperties;
            s.PlaybackInfoChanged += OnPlayback;
            s.TimelinePropertiesChanged += OnTimeline;
            _watched.Add(s);
        }
        catch (Exception ex) { FileLog.Write($"Media watch failed: {ex.GetType().Name}"); }
    }

    void UnwatchAll()
    {
        foreach (var s in _watched)
        {
            try
            {
                s.MediaPropertiesChanged -= OnProperties;
                s.PlaybackInfoChanged -= OnPlayback;
                s.TimelinePropertiesChanged -= OnTimeline;
            }
            catch { }
        }
        _watched.Clear();
    }

    /// <summary>Chooses the session to follow. Returns true if it refreshed (the choice changed, or <paramref name="rewatch"/>).</summary>
    bool Reselect(bool rewatch)
    {
        lock (_gate)
        {
            if (_disposed || _manager is null) return false;
            IReadOnlyList<GlobalSystemMediaTransportControlsSession> all;
            GlobalSystemMediaTransportControlsSession? current = null;
            try { all = _manager.GetSessions(); }
            catch (Exception ex) { FileLog.Write($"Media sessions unreadable: {ex.GetType().Name}"); all = Array.Empty<GlobalSystemMediaTransportControlsSession>(); }
            try { current = _manager.GetCurrentSession(); } catch { }

            if (rewatch)
            {
                UnwatchAll();
                foreach (var s in all) Watch(s);
            }

            var pick = Pick(all, current);
            bool changed = pick is null ? _session is not null : _session is null || !SameApp(pick, _session);
            _session = pick;
            if (!changed && !rewatch) return false;

            if (changed)
            {
                string id = "";
                try { id = pick?.SourceAppUserModelId ?? "none"; } catch { }
                FileLog.Write($"Media session: {id} ({all.Count} available)");
            }
            _ = RefreshAsync(true);
            return true;
        }
    }

    async Task RefreshAsync(bool propertiesChanged)
    {
        int gen = Interlocked.Increment(ref _generation);
        try
        {
            GlobalSystemMediaTransportControlsSession? s;
            lock (_gate) s = _session;
            if (s is null) { _artKey = ""; Publish(null, null, gen); return; }

            var props = await s.TryGetMediaPropertiesAsync();
            string source = s.SourceAppUserModelId;
            var pb = s.GetPlaybackInfo();
            var c = pb.Controls;
            bool playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var info = new MediaInfo(
                props?.Title ?? "", props?.Artist ?? "", playing,
                c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled,
                c.IsNextEnabled, c.IsPreviousEnabled);

            MediaTimeline? timeline = null;
            try
            {
                var t = s.GetTimelineProperties();
                var duration = t.EndTime - t.StartTime;
                if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
                timeline = new MediaTimeline(t.Position - t.StartTime, duration, t.LastUpdatedTime.UtcDateTime, playing);
            }
            catch { /* some apps publish no timeline: no progress bar, no synced lyrics */ }

            // Album art is read only when the track changes, not on every play/pause.
            byte[]? art = null;
            bool artRead = false;
            var key = info.Title + "\u0001" + info.Artist;
            if (props is not null && (propertiesChanged || key != _artKey))
            {
                _artKey = key;
                artRead = true;
                art = await ReadArtAsync(props.Thumbnail);
            }

            if (gen != _generation) return;   // a newer refresh is already on its way
            SourceApp = source;
            Publish(info, timeline, gen, artRead, art);
        }
        catch (Exception ex) { FileLog.Write($"Media refresh failed: {ex.GetType().Name}: {ex.Message}"); Publish(null, null, gen); }
    }

    static async Task<byte[]?> ReadArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > MaxArtBytes) return null;
            using var source = stream.AsStreamForRead();
            using var ms = new MemoryStream((int)stream.Size);
            await source.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    void Publish(MediaInfo? info, MediaTimeline? timeline, int gen, bool artRead = true, byte[]? art = null)
    {
        if (_disposed) return;
        _ui.InvokeAsync(() =>
        {
            if (_disposed || (gen != 0 && gen != _generation)) return;
            Timeline = timeline;
            Changed?.Invoke(info);
            TimelineChanged?.Invoke(timeline);
            if (artRead) ArtChanged?.Invoke(info is { HasTrack: true } ? art : null);
        });
    }

    GlobalSystemMediaTransportControlsSession? Current { get { lock (_gate) return _session; } }

    public async void TogglePlayPause() { try { if (Current is { } s) await s.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (Current is { } s) await s.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (Current is { } s) await s.TrySkipPreviousAsync(); } catch { } }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            UnwatchAll();
            _session = null;
            try
            {
                if (_manager is not null)
                {
                    _manager.CurrentSessionChanged -= OnCurrentChanged;
                    _manager.SessionsChanged -= OnSessionsChanged;
                }
            }
            catch { }
            _manager = null;
        }
    }
}
