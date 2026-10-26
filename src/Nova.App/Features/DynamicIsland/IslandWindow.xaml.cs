using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nova.App.Integrations.Windows;
using Nova.Core.Configuration;
using Nova.Core.Logging;
using Nova.Core.Media;
using Nova.Core.Data;
using NovaMediaTimeline = Nova.Core.Media.MediaTimeline;

namespace Nova.App.Features.DynamicIsland;

/// <summary>
/// The notch. Compact: clock (or the current lyric line), album art and an equalizer while music plays.
/// Expanded (hover, click or shortcut): a Home page with the media player, and a Shelf page that holds files for a moment.
/// </summary>
public partial class IslandWindow : Window
{
    readonly Action _reset, _exit, _focusDock, _openSettings, _openClipboard;
    readonly DispatcherTimer _clock = new();
    readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer _posTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    readonly DispatcherTimer _zTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly FileShelf _shelf = new();
    readonly ScaleTransform[] _bars = new ScaleTransform[4];

    NovaSettings _s;
    MediaInfo? _media;
    NovaMediaTimeline? _timeline;
    IReadOnlyList<LyricLine>? _lyrics;
    ImageSource? _art;
    string? _weather;
    string _clockText = "";
    string? _lyricNow;
    int _lyricIndex = -2;
    bool _expanded, _noticeActive, _placing, _fullscreen;
    bool _hoverOpened, _busy, _dragOver, _selfDrag, _eqOn;
    Point _dragStart;
    string? _dragPath;

    public event Action? MediaPrevious, MediaPlayPause, MediaNext;

    /// <summary>Supplies the status line (CPU/memory/battery). Called only while the Island is expanded.</summary>
    public Func<string>? StatusProvider { get; set; }

    public IslandWindow(NovaSettings settings, bool opaque, Action reset, Action exit, Action focusDock, Action openSettings, Action openClipboard)
    {
        _s = settings; _reset = reset; _exit = exit; _focusDock = focusDock; _openSettings = openSettings; _openClipboard = openClipboard;
        InitializeComponent();
        OverlayChrome.Configure(this, opaque);

        var bars = new[] { Bar1, Bar2, Bar3, Bar4 };
        for (int i = 0; i < bars.Length; i++) { _bars[i] = new ScaleTransform(1, 0.5); bars[i].RenderTransform = _bars[i]; }
        MiniArtImage.Clip = new RectangleGeometry(new Rect(0, 0, 34, 34), 9, 9);
        Pill.SizeChanged += (_, _) => UpdateCompactRadius();   // the pill grows taller when a long lyric wraps: keep it a rounded capsule
        ArtImage.Clip = new RectangleGeometry(new Rect(0, 0, 64, 64), 10, 10);

        _clock.Tick += (_, _) => { UpdateClock(); ScheduleNextMinute(); };
        Loaded += (_, _) => { UpdateClock(); ScheduleNextMinute(); _clock.Start(); _zTimer.Start(); };
        Closed += (_, _) =>
        {
            _clock.Stop(); _noticeTimer.Stop(); _statusTimer.Stop(); _posTimer.Stop();
            _hoverTimer.Stop(); _leaveTimer.Stop(); _zTimer.Stop();
            _faceTimer.Stop(); _extraTimer.Stop(); _usageTimer.Stop();
            AlertSound.Stop();
        };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _posTimer.Tick += (_, _) => TickPosition();
        _zTimer.Tick += (_, _) => { if (IsVisible && !_fullscreen) NativeMethods.BringToTop(this); };   // stay above the top bar
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            var from = CurrentSize();
            _noticeActive = false;
            _alertClick = null;
            UpdateCompactLayout();
            UpdateNotchPadding();
            Morph(from);
            if (HiddenMode && !_expanded) Hide();
        };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (Pill.IsMouseOver && !_expanded && !_fullscreen) { _hoverOpened = true; Expand(focus: false); }
        };
        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            if (_hoverOpened && _expanded && !_busy && !_dragOver && !Pill.IsMouseOver) Collapse();
        };
        Pill.PreviewMouseLeftButtonDown += OnPillDragDown;
        Pill.PreviewMouseMove += OnPillDragMove;
        Pill.PreviewMouseLeftButtonUp += OnPillDragUp;
        SizeChanged += (_, _) => ClipSolidWindow();
        Pill.LostMouseCapture += (_, _) => { _winDragArmed = false; _winDragging = false; };
        InitFace();
        InitExtras();
        InitPages();
        ApplyCore(settings);
    }

    public bool IsExpanded => _expanded;
    bool HiddenMode => _s.IslandBehavior == IslandBehavior.HiddenUntilInvoked;
    bool ReducedMotion => _s.ReducedMotion || !SystemParameters.ClientAreaAnimation;

    public void ApplySettings(NovaSettings s) { ApplyCore(s); EnsureVisibilityForMode(); }

    void ApplyCore(NovaSettings s)
    {
        _s = s;
        TimeText.FontSize = s.ClockFontSize;
        NoticeText.FontSize = Math.Max(12, s.ClockFontSize * 0.7);
        DateText.FontSize = Math.Max(12, s.ClockFontSize * 0.6);
        Pill.AllowDrop = s.ShelfEnabled;
        SubShelf.Visibility = s.ShelfEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!s.ShelfEnabled) { _shelf.Clear(); RebuildShelf(); if (_sub == 2) _sub = 0; }
        if (!s.LyricsEnabled) { _lyrics = null; _lyricIndex = -2; }
        ApplyNotchLook();
        ApplyFace();
        UpdateClock();
        RefreshExtras();
        UpdateExtraTimer();
        UpdateUsageTimer();
        LockButton.Visibility = Visibility.Visible;
        Reposition();
    }

    // ---- media, lyrics, weather, status ----

    public void SetMedia(MediaInfo? info)
    {
        var from = CurrentSize();
        _media = info;
        if (info is not { HasTrack: true }) { _lyrics = null; _lyricIndex = -2; }
        RefreshExtras();
        if (!_expanded && IsVisible) Morph(from);
    }

    public void SetWeather(string? text) { _weather = text; RefreshExtras(); }

    /// <summary>Name of the app that is playing (Spotify, Chrome...), from its Windows app id.</summary>
    public void SetMediaSource(string? appId)
    {
        var s = appId ?? "";
        int bang = s.IndexOf('!');
        if (bang >= 0) s = s[(bang + 1)..];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        int us = s.IndexOf('_');
        if (us > 0) s = s[..us];
        MediaSource.Text = s.Length == 0 ? "" : char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>Album art as encoded image bytes (null = none).</summary>
    public void SetArt(byte[]? bytes)
    {
        _art = null;
        if (bytes is { Length: > 0 })
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = 128;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                _art = bi;
            }
            catch { _art = null; }
        }
        ArtImage.Source = _art;
        MiniArtImage.Source = _art;
    }

public void SetTimeline(NovaMediaTimeline? t)
{
    _timeline = t;
    SyncPosition();
    UpdatePosTimer();
}

    /// <summary>Synced lyric lines for the current track (null = none).</summary>
    public void SetLyrics(IReadOnlyList<LyricLine>? lines)
    {
        _lyrics = _s.LyricsEnabled ? lines : null;
        _lyricIndex = -2;
        SyncPosition();
        UpdatePosTimer();
    }

    void RefreshExtras()
    {
        bool hasMedia = _s.ShowMedia && _media is { HasTrack: true };
        bool playing = hasMedia && _media!.IsPlaying;

        MediaCard.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
        NoMusicText.Visibility = hasMedia ? Visibility.Collapsed : Visibility.Visible;
        NoMusicText.Text = _s.ShowMedia
            ? "Tidak ada musik yang diputar. Putar lagu di Spotify, browser, atau pemutar lain."
            : "Kontrol musik dimatikan. Aktifkan “Media controls” di Settings NOVA.";
        UpdateLyricEnable();
        MiniArt.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
        Eq.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        MiniPlay.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
        if (hasMedia)
        {
            var m = _media!;
            MiniPlay.Content = m.IsPlaying ? "\uE769" : "\uE768";
            MiniPlay.IsEnabled = m.CanPlayPause;
            AutomationProperties.SetName(MiniPlay, m.IsPlaying ? "Jeda musik" : "Putar musik");
            MediaTitle.Text = m.Title.Trim();
            MediaArtist.Text = m.Artist.Trim();
            PrevButton.IsEnabled = m.CanPrevious;
            NextButton.IsEnabled = m.CanNext;
            PlayButton.IsEnabled = m.CanPlayPause;
            PlayButton.Content = m.IsPlaying ? "\uE769" : "\uE768";
            AutomationProperties.SetName(PlayButton, m.IsPlaying ? "Pause playback" : "Resume playback");
            AutomationProperties.SetName(MediaCard, "Media controls: " + m.Describe() + (m.IsPlaying ? "" : ", paused"));
        }
        else SetLyricText(null, null);
        SetEqualizer(playing);

        bool hasWeather = _s.WeatherEnabled && !string.IsNullOrWhiteSpace(_weather);
        WeatherText.Visibility = hasWeather ? Visibility.Visible : Visibility.Collapsed;
        if (hasWeather) WeatherText.Text = _weather;

        ClipboardButton.Visibility = _s.ClipboardHistoryEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!_s.ShowSystemStatus) StatusText.Visibility = Visibility.Collapsed;
        if (_expanded) StartStatus();

        SyncPosition();
        UpdatePosTimer();
        UpdateCompactText();
        UpdateWave();
    }

    void SetEqualizer(bool on)
    {
        if (on == _eqOn) return;
        _eqOn = on;
        for (int i = 0; i < _bars.Length; i++)
        {
            if (on && !ReducedMotion)
            {
                var anim = new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(420 + i * 130))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                _bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            else
            {
                _bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _bars[i].ScaleY = on ? 0.7 : 0.5;
            }
        }
    }

    // Progress bar and synced lyrics follow the playback position; the timer only runs while something is visible that needs it.
    void UpdatePosTimer()
    {
        bool need = _s.ShowMedia && _media is { HasTrack: true, IsPlaying: true } && _timeline is not null &&
                    (_expanded || _lyrics is { Count: > 0 });
        if (need && !_posTimer.IsEnabled) _posTimer.Start();
        else if (!need && _posTimer.IsEnabled) _posTimer.Stop();
    }

    void TickPosition() => SyncPosition();

    void SyncPosition()
    {
        if (_timeline is null) { Progress.Value = 0; UpdateLyrics(TimeSpan.Zero); return; }
        var now = DateTime.UtcNow;
        Progress.Value = _timeline.Fraction(now);
        UpdateLyrics(_timeline.Now(now));
    }

    void UpdateLyrics(TimeSpan position)
    {
        if (!_s.LyricsEnabled || _lyrics is not { Count: > 0 } lines) { SetLyricText(null, null); return; }
        int i = Lrc.IndexAt(lines, position);
        if (i == _lyricIndex) return;
        _lyricIndex = i;
        string current = i >= 0 ? lines[i].Text : "";
        if (string.IsNullOrWhiteSpace(current)) current = "♪";   // an empty line is an instrumental break
        string? next = i + 1 < lines.Count ? lines[i + 1].Text : null;
        SetLyricText(current, string.IsNullOrWhiteSpace(next) ? null : next);
    }

    void SetLyricText(string? current, string? next)
    {
        var from = CurrentSize();
        _lyricNow = current;
        LyricPanel.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
        LyricText.Text = current ?? "";
        LyricNext.Text = next ?? "";
        LyricNext.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateCompactText();
        if (!_expanded && IsVisible) Morph(from);   // a longer line makes the Island wider / taller, a shorter one shrinks it again
    }

    /// <summary>
    /// What the compact Island shows, most urgent first: the running timer, the current lyric line (it wraps onto more lines when it is long,
    /// and the Island grows with it), the song title with the artist underneath, then the day, date and time.
    /// </summary>
    void UpdateCompactText()
    {
        var pomo = _expanded ? null : PomoCompactText();
        bool hasMedia = _s.ShowMedia && _media is { HasTrack: true };
        bool playing = hasMedia && _media!.IsPlaying;
        bool showLyric = pomo is null && _lyricNow is not null && !_expanded && playing;
        bool showTrack = pomo is null && !showLyric && !_expanded && playing;
        double size = _s.ClockFontSize;
        double small = Math.Max(12, size * 0.75);

        string? sub = null;
        if (pomo is not null) { TimeText.Text = pomo; }
        else if (showLyric)
        {
            TimeText.Text = _lyricNow;
            var m = _media!;
            sub = string.IsNullOrWhiteSpace(m.Artist) ? m.Title.Trim() : m.Title.Trim() + " · " + m.Artist.Trim();
        }
        else if (showTrack)
        {
            TimeText.Text = _media!.Title.Trim();
            sub = _media.Artist.Trim();
        }
        else TimeText.Text = _s.IslandFaceMode == IslandFaceMode.AllModules && !_expanded && _moduleText is not null ? _moduleText : _clockText;

        bool left = showLyric || showTrack;
        TimeText.Width = double.NaN;      // hug the text: no empty room left and right of it
        TimeText.MaxWidth = left ? 270 : 260;
        TimeText.FontSize = pomo is not null || left ? small : size;
        TimeText.TextWrapping = showLyric ? TextWrapping.Wrap : TextWrapping.NoWrap;
        // A long lyric may use up to 3 lines (then it ends with "…"); everything else stays on one line.
        TimeText.MaxHeight = showLyric ? Math.Ceiling(small * 1.4 * 3) + 2 : double.PositiveInfinity;
        TimeText.TextAlignment = left ? TextAlignment.Left : TextAlignment.Center;
        TimeText.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        TextCol.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center;

        // Plain clock: the little face may take its place, or a one-line CPU / RAM reading may sit under it.
        bool plain = pomo is null && !showLyric && !showTrack;
        bool showFace = plain && !_expanded && _faceVisible && _s.IslandFaceMode != IslandFaceMode.ClockOnly;
        ShowFace(showFace);
        if (plain && !showFace && !_expanded && _s.IslandShowUsage && _usageText.Length > 0) sub = _usageText;
        SubText.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        SubText.TextAlignment = left ? TextAlignment.Left : TextAlignment.Center;

        SubText.Text = sub ?? "";
        SubText.MaxWidth = 270;
        SubText.Visibility = string.IsNullOrWhiteSpace(sub) ? Visibility.Collapsed : Visibility.Visible;
    }

    void StartStatus() { UpdateStatus(); _statusTimer.Start(); }

    void UpdateStatus()
    {
        RefreshUsage();
        if (!_s.ShowSystemStatus || StatusProvider is null) { StatusText.Visibility = Visibility.Collapsed; return; }
        string text;
        try { text = StatusProvider(); } catch { text = ""; }
        StatusText.Text = text;
        StatusText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- visibility / expansion ----

    /// <summary>Shows or hides the pill according to the configured behavior. Never activates the window.</summary>
    public void EnsureVisibilityForMode()
    {
        if (_fullscreen) { if (IsVisible && !_expanded) Hide(); return; }   // stay out of the way of full-screen apps and games
        if (HiddenMode) { if (IsVisible && !_expanded && !_noticeActive) Hide(); }
        else if (!IsVisible) { Reposition(); Show(); }
    }

    /// <summary>Hotkey / tray entry point: reveals the pill if hidden, then toggles expansion.</summary>
    public void Invoke()
    {
        if (!IsVisible) { Reposition(); Show(); }
        Toggle();
    }

    public void Toggle() { if (_expanded) Collapse(); else Expand(focus: true); }

    /// <summary>
    /// Brief, non-focus-stealing message. Informational notices respect the "temporary notices" setting;
    /// important ones (errors from explicit user actions) always show.
    /// </summary>
    public void ShowNotice(string text, bool important = false, int seconds = 5, string? glyph = null, string? badgeColor = null, Action? onClick = null)
    {
        if (!important && !_s.TemporaryNotices) return;
        var from = CurrentSize();
        _noticeTimer.Interval = TimeSpan.FromSeconds(Math.Min(30, Math.Max(2, seconds)));
        NoticeText.Text = text;
        NoticeGlyph.Text = glyph ?? "";
        NoticeBadge.Visibility = glyph is null ? Visibility.Collapsed : Visibility.Visible;
        if (glyph is not null)
        {
            try { NoticeBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeColor ?? "#4C8DFF")); }
            catch { NoticeBadge.Background = Brushes.DimGray; }
        }
        _alertClick = onClick;
        _noticeActive = true;
        UpdateCompactLayout();
        UpdateNotchPadding();
        if (!IsVisible) { Reposition(); Show(); }
        else Morph(from);
        var peer = UIElementAutomationPeer.FromElement(NoticeText) ?? UIElementAutomationPeer.CreatePeerForElement(NoticeText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    Action? _alertClick;

    /// <summary>
    /// What fills the compact notch: an alert (replacing the clock for a few seconds) or the clock / lyric row.
    /// Expanded: the alert, if any, sits above the pages and the clock row is gone (the header shows the time).
    /// </summary>
    void UpdateCompactLayout()
    {
        NoticeRow.Visibility = _noticeActive ? Visibility.Visible : Visibility.Collapsed;
        NoticeRow.Margin = _expanded ? new Thickness(0, 4, 0, 0) : new Thickness(0);
        TimeRow.Visibility = _expanded || _noticeActive ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <param name="focus">true for an explicit request (shortcut, click): the Island may take keyboard focus.
    /// false for hover or a file drag: it grows but never takes focus.</param>
    void Expand(bool focus)
    {
        if (_expanded) { if (focus) PinOpen(); return; }
        var from = CurrentSize();
        _expanded = true;
        if (focus) _hoverOpened = false;
        ApplyCorners();
        UpdateNotchPadding();
        UpdateCompactLayout();   // the expanded header shows the time itself
        ExpandedPanel.Visibility = Visibility.Visible;
        OpenAdaptivePage();
        UpdateQuickTimer();
        UpdateWave();
        UpdateCompactText();
        if (ReducedMotion) ExpandedPanel.BeginAnimation(OpacityProperty, null);
        else ExpandedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        StartStatus();
        UpdateExtraTimer();
        UpdateUsageTimer();
        SyncPosition();
        UpdatePosTimer();
        Morph(from);
        if (focus)
        {
            NativeMethods.SetOverlayStyle(this, noActivate: false);   // explicit user request: allow keyboard focus
            Activate();
            DockButton.Focus();
        }
    }

    void Collapse()
    {
        if (!_expanded) return;
        var from = CurrentSize();
        _expanded = false;
        _hoverOpened = false;
        _hoverTimer.Stop(); _leaveTimer.Stop();
        ApplyCorners();
        UpdateNotchPadding();
        _statusTimer.Stop();                       // no background work while compact
        UpdateExtraTimer();
        UpdateUsageTimer();
        ExpandedPanel.Visibility = Visibility.Collapsed;
        CloseRadio(morph: false);
        UpdateCompactLayout();
        NativeMethods.SetOverlayStyle(this, noActivate: true);
        UpdateCompactText();
        UpdateQuickTimer();
        UpdatePosTimer();
        UpdateWave();
        Morph(from);
        if (HiddenMode && !_noticeActive) Hide();
    }

    /// <summary>A click while the Island was opened by hovering: keep it open (and let it receive keyboard focus).</summary>
    void PinOpen()
    {
        _hoverOpened = false;
        _leaveTimer.Stop();
        NativeMethods.SetOverlayStyle(this, noActivate: false);
        Activate();
    }

    // ---- smooth grow / shrink ----

    Size CurrentSize() => new(Pill.ActualWidth, Pill.ActualHeight);

    /// <summary>Animates the pill from its old size to the size its new content needs.</summary>
    void Morph(Size from)
    {
        Pill.BeginAnimation(WidthProperty, null);
        Pill.BeginAnimation(HeightProperty, null);
        Pill.Width = double.NaN;
        Pill.Height = double.NaN;
        if (ReducedMotion || !IsVisible || from.Width <= 0 || from.Height <= 0) return;

        Pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var to = Pill.DesiredSize;
        if (Math.Abs(to.Width - from.Width) < 1 && Math.Abs(to.Height - from.Height) < 1) return;

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var w = new DoubleAnimation(from.Width, to.Width, duration) { EasingFunction = ease };
        var h = new DoubleAnimation(from.Height, to.Height, duration) { EasingFunction = ease };
        // Drop the animation at the end so the pill goes back to sizing itself to its content.
        w.Completed += (_, _) => Pill.BeginAnimation(WidthProperty, null);
        h.Completed += (_, _) => Pill.BeginAnimation(HeightProperty, null);
        Pill.BeginAnimation(WidthProperty, w);
        Pill.BeginAnimation(HeightProperty, h);
    }

    // ---- shelf (temporary files) ----

    void RebuildShelf()
    {
        ShelfItems.Children.Clear();
        foreach (var path in _shelf.Items) ShelfItems.Children.Add(BuildChip(path));
        ShelfHint.Visibility = _shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfClearButton.Visibility = _shelf.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SubShelf.Content = _shelf.Count > 0 ? $"Rak file · {_shelf.Count}" : "Rak file";
    }

    Border BuildChip(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path;
        var stack = new StackPanel { Width = 72 };
        stack.Children.Add(new System.Windows.Controls.Image { Width = 32, Height = 32, Source = IconLoader.Load(path), HorizontalAlignment = HorizontalAlignment.Center });
        var label = new TextBlock
        {
            Text = name, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        stack.Children.Add(label);

        var chip = new Border
        {
            Child = stack, Padding = new Thickness(4), Margin = new Thickness(2), CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent, ToolTip = path, Cursor = Cursors.Hand
        };
        AutomationProperties.SetName(chip, "Item rak " + name);
        chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "NovaSurfaceAlt");
        chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
        chip.PreviewMouseLeftButtonDown += (_, e) => { _dragStart = e.GetPosition(null); _dragPath = path; };
        chip.PreviewMouseMove += OnChipMouseMove;
        chip.MouseLeftButtonUp += (_, e) => { if (_dragPath == path) OpenPath(path); _dragPath = null; e.Handled = true; };
        chip.ContextMenu = BuildChipMenu(path);
        return chip;
    }

    ContextMenu BuildChipMenu(string path)
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Buka" };
        open.Click += (_, _) => OpenPath(path);
        var show = new MenuItem { Header = "Tampilkan di folder" };
        show.Click += (_, _) => ShowInFolder(path);
        var remove = new MenuItem { Header = "Hapus dari rak" };
        remove.Click += (_, _) => { _shelf.Remove(path); RebuildShelf(); };
        menu.Items.Add(open); menu.Items.Add(show); menu.Items.Add(new Separator()); menu.Items.Add(remove);
        menu.Opened += (_, _) => _busy = true;
        menu.Closed += (_, _) => { _busy = false; if (_hoverOpened) _leaveTimer.Start(); };
        return menu;
    }

    void OnChipMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragPath is null) return;
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var path = _dragPath;
        _dragPath = null;
        _selfDrag = true; _busy = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy | DragDropEffects.Link);   // the original stays where it is
        }
        catch (Exception ex) { FileLog.Write($"Shelf drag failed: {ex.GetType().Name}"); }
        finally
        {
            _selfDrag = false; _busy = false;
            if (_hoverOpened) _leaveTimer.Start();
        }
    }

    void OpenPath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            _shelf.Remove(path); RebuildShelf();
            ShowNotice("File itu sudah tidak ada, jadi dihapus dari rak.", important: true);
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { FileLog.Write($"Shelf open failed: {ex.GetType().Name}"); ShowNotice("File tidak bisa dibuka.", important: true); }
    }

    void ShowInFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { FileLog.Write($"Show in folder failed: {ex.GetType().Name}"); }
    }

    void OnShelfClear(object sender, RoutedEventArgs e) { _shelf.Clear(); RebuildShelf(); }

    bool CanAccept(DragEventArgs e) => _s.ShelfEnabled && !_selfDrag && e.Data.GetDataPresent(DataFormats.FileDrop);

    void SetShelfHighlight(bool on) => ShelfBox.SetResourceReference(Border.BorderBrushProperty, on ? "NovaAccent" : "NovaBorder");

    void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!CanAccept(e)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        _dragOver = true;
        _leaveTimer.Stop(); _hoverTimer.Stop();
        if (!_expanded) { _hoverOpened = true; Expand(focus: false); }
        ShowShelf();
        SetShelfHighlight(true);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanAccept(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDragLeave(object sender, DragEventArgs e)
    {
        _dragOver = false;
        SetShelfHighlight(false);
        if (_hoverOpened) _leaveTimer.Start();
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        _dragOver = false;
        SetShelfHighlight(false);
        if (!CanAccept(e) || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var f in files) _shelf.Add(f);
        RebuildShelf();
        ShowShelf();
        e.Handled = true;
        if (_hoverOpened) _leaveTimer.Start();
    }

    // ---- Mac-style notch look ----

    bool NotchActive => _s.NotchMode;
    /// <summary>The older look: a bar across the screen with the Island attached like a notch (square top corners, flush with the edge).</summary>
    bool FlushNotch => NotchActive && _s.ShowTopBar;

    /// <summary>
    /// Notch mode: pure black, square top corners flush with the screen edge, rounded bottom corners, exactly as tall as the
    /// top bar in its compact state. Normal mode: the original floating rounded pill. High contrast always wins over black.
    /// </summary>
    void ApplyNotchLook()
    {
        Pill.Resources.Clear();
        bool highContrast = SystemParameters.HighContrast || _s.HighContrastOverride;

        if (NotchActive)
        {
            double bar = NotchLayout.BarHeightDip(_s.ClockFontSize);
            Pill.BorderThickness = new Thickness(0);
            // Flush notch: exactly the bar's height. Floating Island: a little taller, and it grows with what it shows (wrapped lyrics).
            Pill.MinHeight = FlushNotch ? bar : bar + 6;
            TimeRow.Height = double.NaN;
            TimeRow.MinHeight = FlushNotch ? bar : bar - 2;
            NoticeRow.MinHeight = TimeRow.MinHeight;
            if (!highContrast)
            {
                Pill.Resources["NovaSurface"] = Frozen(Color.FromRgb(0, 0, 0));
                Pill.Resources["NovaOnSurface"] = Frozen(Colors.White);
                Pill.Resources["NovaSurfaceAlt"] = Frozen(Color.FromRgb(0x2A, 0x2A, 0x30));
                Pill.Resources["NovaBorder"] = Frozen(Color.FromRgb(0x8A, 0x8A, 0x94));
            }
        }
        else
        {
            Pill.BorderThickness = new Thickness(1);
            Pill.MinHeight = 0;
            Pill.MinWidth = 0;
            TimeRow.Height = double.NaN;
            TimeRow.MinHeight = 0;
            NoticeRow.MinHeight = 0;
        }
        ApplyCorners();
        UpdateNotchPadding();

        // The opaque fallback window has no transparency, so its own background must match the notch.
        if (!AllowsTransparency)
            Background = NotchActive && !highContrast
                ? Brushes.Black
                : Application.Current.Resources["NovaWindow"] as Brush ?? SystemColors.WindowBrush;
    }

    void ApplyCorners()
    {
        double r = _expanded ? 28 : NotchActive && !FlushNotch ? CompactRadius() : 22;
        Pill.CornerRadius = FlushNotch ? new CornerRadius(0, 0, r, r) : new CornerRadius(r);
        ApplyGlassLook();
    }

    /// <summary>Half the pill's height (a capsule), but never more than 30: a tall, wrapped lyric becomes a rounded card, not a circle.</summary>
    double CompactRadius()
    {
        double h = Pill.ActualHeight > 0 ? Pill.ActualHeight : NotchLayout.BarHeightDip(_s.ClockFontSize) + 6;
        return Math.Clamp(h / 2, 14, 30);
    }

    /// <summary>Solid-window fallback only: cuts the window's square corners off so it looks like the rounded pill.</summary>
    void ClipSolidWindow()
    {
        if (AllowsTransparency) return;
        NativeMethods.ApplyRoundShape(this, FlushNotch ? 0 : Pill.CornerRadius.TopLeft);
    }

    void UpdateCompactRadius()
    {
        if (_expanded || !NotchActive || FlushNotch) return;
        double r = CompactRadius();
        if (Math.Abs(Pill.CornerRadius.TopLeft - r) > 0.5) Pill.CornerRadius = new CornerRadius(r);
    }

    /// <summary>Floating Island and expanded notch: dark navy glass with a soft blue edge, as in the concept. The flush compact notch stays plain black.</summary>
    void ApplyGlassLook()
    {
        bool highContrast = SystemParameters.HighContrast || _s.HighContrastOverride;
        double k = highContrast ? 1 : Math.Clamp(_s.IslandOpacity, 0.2, 1.0);   // only the background fades; text and icons stay sharp
        if (!AllowsTransparency)
        {
            // Solid-window fallback: the whole window fades (never below half, so text stays readable) and is clipped to the pill's shape.
            NativeMethods.SetWindowAlpha(this, Math.Max(0.5, k));
            Dispatcher.BeginInvoke(new Action(ClipSolidWindow), DispatcherPriority.Loaded);
        }
        if (NotchActive && !highContrast && (_expanded || !FlushNotch))
        {
            var fill = _expanded
                ? new LinearGradientBrush(Color.FromRgb(0, 0, 0), Color.FromRgb(0x0B, 0x14, 0x24), 90)
                : new LinearGradientBrush(Color.FromRgb(0x05, 0x08, 0x12), Color.FromRgb(0x0C, 0x16, 0x2C), 90);
            fill.Freeze();
            Pill.Background = Fade(fill, k);
            Pill.BorderThickness = FlushNotch ? new Thickness(1, 0, 1, 1) : new Thickness(1);
            Pill.BorderBrush = Frozen(Color.FromArgb(_expanded ? (byte)0x66 : (byte)0x55, 0x4C, 0x8D, 0xFF));
        }
        else
        {
            if (k < 0.999 && Pill.TryFindResource("NovaSurface") is Brush surface) Pill.Background = Fade(surface, k);
            else Pill.SetResourceReference(Border.BackgroundProperty, "NovaSurface");
            Pill.SetResourceReference(Border.BorderBrushProperty, "NovaBorder");
            if (NotchActive) Pill.BorderThickness = new Thickness(0);
        }
    }

    /// <summary>The same brush with its alpha multiplied by <paramref name="k"/> (1 = unchanged).</summary>
    static Brush Fade(Brush b, double k)
    {
        if (k >= 0.999) return b;
        switch (b)
        {
            case SolidColorBrush s:
            {
                var c = s.Color;
                c.A = (byte)Math.Round(c.A * k);
                return Frozen(c);
            }
            case GradientBrush g:
            {
                var clone = g.CloneCurrentValue();
                foreach (var stop in clone.GradientStops)
                {
                    var c = stop.Color;
                    c.A = (byte)Math.Round(c.A * k);
                    stop.Color = c;
                }
                clone.Freeze();
                return clone;
            }
            default: return b;
        }
    }

    void UpdateNotchPadding()
    {
        if (NotchActive)
            Pill.Padding = FlushNotch ? new Thickness(14, 0, 14, _expanded ? 8 : 0)
                                      : new Thickness(14, _expanded ? 8 : 4, 14, _expanded ? 8 : 4);
        else Pill.Padding = new Thickness(14, 5, 14, 5);
    }

    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>Re-reads theme-dependent colors (Windows theme or high-contrast change).</summary>
    public void RefreshTheme() => ApplyNotchLook();

    /// <summary>A full-screen app or game started/stopped: the Island hides itself so it never covers it.</summary>
    public void SetFullscreen(bool fullscreen)
    {
        _fullscreen = fullscreen;
        EnsureVisibilityForMode();
    }

    /// <summary>Keeps the notch above the top bar it overlaps.</summary>
    public void BringToFront() { if (IsVisible && !_fullscreen) NativeMethods.BringToTop(this); }

    // ---- clock ----

    void UpdateClock()
    {
        var now = DateTime.Now;
        var c = CultureInfo.CurrentCulture;
        var time = now.ToString(_s.ClockFormat == ClockFormat.H12 ? "h:mm tt" : "HH:mm", c);
        // With the full top bar the bar already shows the date, so the notch only needs the time. Island-only: day, date and time.
        var day = now.ToString("ddd, d MMM", c);
        _clockText = FlushNotch ? time : NotchActive ? day + " · " + time : time + " · " + day;
        DateText.Text = now.ToString("dddd, d MMMM yyyy", c);
        HdrTime.Text = now.ToString(_s.ClockFormat == ClockFormat.H12 ? "h:mm tt" : "HH:mm", c);
        HdrDate.Text = now.ToString("ddd, d MMM", c);
        UpdateCompactText();
    }

    // Minute-resolution clock: wake once per minute instead of polling.
    void ScheduleNextMinute()
    {
        var now = DateTime.Now;
        _clock.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
    }

    // ---- placement ----

    /// <summary>Re-centres on the configured monitor. Also called after monitors or scaling change.</summary>
    public void Relocate() => Reposition();

    void Reposition()
    {
        if (_placing) return;
        _placing = true;
        try
        {
            // Notch: flush with the very top of the monitor (on top of the bar). Otherwise: floating below the work-area top.
            // The full-bar notch is always flush and centred; the floating Island sits where it was dragged to.
            double ox = FlushNotch ? 0 : _s.IslandOffsetX;
            if (NotchActive) WindowPlacement.PlaceCentered(this, _s.IslandMonitor, PlacementEdge.TopFlush, FlushNotch ? 0 : _s.IslandTopOffset, ox);
            else WindowPlacement.PlaceCentered(this, _s.IslandMonitor, PlacementEdge.Top, _s.IslandTopOffset, ox);
        }
        finally { _placing = false; }
    }

    // ---- events ----

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        NativeMethods.SetOverlayStyle(this, noActivate: true);
        // Two-finger sideways scrolling on a touchpad arrives as WM_MOUSEHWHEEL, which WPF does not turn into an event.
        System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(HWheelHook);
    }

    DateTime _lastHWheel = DateTime.MinValue;

    IntPtr HWheelHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEHWHEEL = 0x020E;
        if (msg == WM_MOUSEHWHEEL && _expanded)
        {
            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            if (Math.Abs(delta) >= 40 && (DateTime.UtcNow - _lastHWheel).TotalMilliseconds > 450)   // one swipe = one page
            {
                _lastHWheel = DateTime.UtcNow;
                GoToPage(_pageIndex + (delta > 0 ? 1 : -1));
            }
            handled = true;
        }
        return IntPtr.Zero;
    }
    void OnSizeChanged(object sender, SizeChangedEventArgs e) => Reposition();

    // ---- dragging the floating Island to another spot ----

    /// <summary>Raised once when a drag ends: the new sideways offset and distance from the top (device-independent pixels), for saving.</summary>
    public event Action<double, double>? PositionChanged;

    bool _winDragArmed, _winDragging;
    int _dragCursorX, _dragCursorY, _dragWinX, _dragWinY;

    static bool IsInteractive(DependencyObject? d, DependencyObject stop)
    {
        while (d is not null && !ReferenceEquals(d, stop))
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.RangeBase or System.Windows.Controls.Primitives.ScrollBar
                or System.Windows.Controls.Primitives.Selector or ScrollViewer) return true;
            if (d is FrameworkElement { Cursor: var c } && ReferenceEquals(c, Cursors.Hand)) return true;   // clickable rows and shelf chips
            d = (d is Visual or System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    void OnPillDragDown(object sender, MouseButtonEventArgs e)
    {
        _winDragArmed = false;
        if (FlushNotch || _busy || _dragOver) return;   // the bar notch is part of the bar: it stays put
        if (IsInteractive(e.OriginalSource as DependencyObject, Pill)) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.TryGetCursor(out _dragCursorX, out _dragCursorY) || !NativeMethods.GetWindowRect(handle, out var r)) return;
        _dragWinX = r.Left; _dragWinY = r.Top;
        _winDragArmed = true;
    }

    void OnPillDragMove(object sender, MouseEventArgs e)
    {
        if (!_winDragArmed) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndPillDrag(save: _winDragging); return; }
        if (!NativeMethods.TryGetCursor(out var cx, out var cy)) return;
        int dx = cx - _dragCursorX, dy = cy - _dragCursorY;
        if (!_winDragging)
        {
            if (Math.Abs(dx) < 8 && Math.Abs(dy) < 8) return;   // a small wobble is still a click
            _winDragging = true;
            _hoverTimer.Stop(); _leaveTimer.Stop();
            Pill.CaptureMouse();
        }

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetWindowRect(handle, out var r)) return;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var bounds = MonitorService.Resolve(_s.IslandMonitor).Bounds;   // stays on its own monitor
        int x = Math.Clamp(_dragWinX + dx, bounds.Left, Math.Max(bounds.Left, bounds.Right - w));
        int y = Math.Clamp(_dragWinY + dy, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - h));
        NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        StoreOffsets(x, y, w);
        e.Handled = true;
    }

    void OnPillDragUp(object sender, MouseButtonEventArgs e)
    {
        bool moved = _winDragging;
        EndPillDrag(save: moved);
        if (moved) e.Handled = true;   // it was a drag, not a click: do not expand or collapse
    }

    void EndPillDrag(bool save)
    {
        _winDragArmed = false;
        bool was = _winDragging;
        _winDragging = false;
        if (Pill.IsMouseCaptured) Pill.ReleaseMouseCapture();
        if (save && was) PositionChanged?.Invoke(_s.IslandOffsetX, _s.IslandTopOffset);
    }

    /// <summary>Turns the window's pixel position into the settings' offsets (the inverse of <see cref="WindowPlacement.PlaceCentered"/>).</summary>
    void StoreOffsets(int xPx, int yPx, int wPx)
    {
        var scr = MonitorService.Resolve(_s.IslandMonitor);
        var wa = NotchActive ? scr.Bounds : scr.WorkingArea;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0) dpi = 1;
        _s.IslandOffsetX = Math.Round((xPx - (wa.Left + (wa.Width - wPx) / 2)) / dpi);
        _s.IslandTopOffset = Math.Max(0, Math.Round((yPx - wa.Top) / dpi));
    }

    void OnPillEnter(object sender, MouseEventArgs e)
    {
        _leaveTimer.Stop();
        if (_s.IslandHoverExpand && !_expanded && !_fullscreen) _hoverTimer.Start();
    }

    void OnPillLeave(object sender, MouseEventArgs e)
    {
        _hoverTimer.Stop();
        if (_expanded && _hoverOpened) _leaveTimer.Start();
    }

    void OnPillClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        if (!_expanded && _noticeActive && _alertClick is { } go)
        {
            _alertClick = null;
            _noticeTimer.Stop();
            _noticeTimer.Interval = TimeSpan.FromMilliseconds(10);
            _noticeTimer.Start();   // dismiss the alert, then do what it offers (e.g. bring WhatsApp forward)
            go();
            return;
        }
        if (!_expanded) Expand(focus: true);
        else if (_hoverOpened) PinOpen();
        else Collapse();
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _expanded) { Collapse(); e.Handled = true; }
        else if (HandlePageKey(e)) e.Handled = true;
    }

    void OnDeactivated(object? sender, EventArgs e)
    {
        if (_busy || _dragOver) return;
        Collapse();   // outside click / focus moved away
    }

    void OnDockClick(object sender, RoutedEventArgs e) { Collapse(); _focusDock(); }
    void OnLockClick(object sender, RoutedEventArgs e)
    {
        Collapse();
        if (!NativeMethods.Lock()) FileLog.Write("Lock screen refused by Windows.");
    }
    void OnClipboardClick(object sender, RoutedEventArgs e) { Collapse(); _openClipboard(); }
    void OnSettingsClick(object sender, RoutedEventArgs e) { Collapse(); _openSettings(); }
    void OnResetClick(object sender, RoutedEventArgs e) { Collapse(); _reset(); }
    void OnExitClick(object sender, RoutedEventArgs e) => _exit();
    void OnPrevClick(object sender, RoutedEventArgs e) => MediaPrevious?.Invoke();
    void OnPlayClick(object sender, RoutedEventArgs e) => MediaPlayPause?.Invoke();
    void OnNextClick(object sender, RoutedEventArgs e) => MediaNext?.Invoke();
}
