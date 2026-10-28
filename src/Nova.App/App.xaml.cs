using System.Windows;
using Nova.App.Features.ClipboardPanel;
using Nova.App.Features.DynamicIsland;
using Nova.App.Features.FloatingDock;
using Nova.App.Features.Launcher;
using Nova.App.Features.TopBar;
using Nova.App.Integrations.Windows;
using Nova.App.Settings;
using Nova.App.Themes;
using Nova.Core.Configuration;
using Nova.Core.Logging;
using Nova.Core.Media;
using Nova.Core.Data;
using Nova.Core.Notifications;
using Nova.Core.Reminders;

namespace Nova.App;

public partial class App : Application
{
    const string MutexName = "Nova.SingleInstance.v1";

    Mutex? _mutex;
    SettingsStore _store = new();
    NovaSettings _settings = new();
    IslandWindow? _island;
    DockWindow? _dock;
    TopBarWindow? _topBar;
    DockReserveWindow? _topReserve;   // island-only mode: an invisible strip that keeps windows below the Island
    AppAlertService? _alerts;
    WindowHarmonyService? _harmony;
    WindowShapeService? _shape;
    SystemChangesService? _system;
    HotkeyService? _hotkey;
    readonly List<HotkeyService> _pageHotkeys = new();
    HotkeyService? _toggleHotkey;   // hides / shows the whole Island
    HotkeyService? _launcherHotkey; // opens the quick launcher
    HotkeyService? _translateHotkey; // opens the "Terjemah" page in the notch
    HotkeyService? _askHotkey;      // opens the "Tanya" page (search / ask) in the notch
    LauncherWindow? _launcher;
    TrayService? _tray;
    SettingsWindow? _settingsWindow;
    ClipboardWindow? _clipboardWindow;
    ClipboardMonitor? _clipboard;
    MediaService? _media;
    WeatherService? _weather;
    readonly SystemStatusService _status = new();
    MediaInfo? _lastTrack, _lyricsTrack;
    readonly LyricsService _lyricsService = new();
    CancellationTokenSource? _lyricsCts;
    bool _safeMode;
    readonly System.Windows.Threading.DispatcherTimer _taskbarGuard = new() { Interval = TimeSpan.FromSeconds(3) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var firstInstance);
        if (!firstInstance) { _mutex.Dispose(); _mutex = null; Shutdown(); return; }

        DispatcherUnhandledException += (_, a) =>
        {
            FileLog.Write($"Unhandled UI exception: {a.Exception.GetType().Name}: {a.Exception.Message}");
            a.Handled = true;   // degrade, don't crash the whole app
        };

        _safeMode = e.Args.Contains("--safe-mode", StringComparer.OrdinalIgnoreCase);
        if (_safeMode) _store = new SettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nova-safe.json"));

        var load = _safeMode ? new LoadResult(new NovaSettings(), null) : _store.Load();
        _settings = load.Settings;
        if (_safeMode)   // safe mode: only clock + dock, and NOVA touches nothing in Windows itself
        { _settings.ShowMedia = false; _settings.ShowSystemStatus = false; _settings.NotchMode = false; _settings.HideTaskbar = false; _settings.WindowsDarkMode = false; _settings.ShowRunningApps = false; _settings.DockReservesSpace = false; _settings.LyricsEnabled = false; }
        if (load.Warning is not null) FileLog.Write(load.Warning);
        if (!_safeMode)
        {
            // NOVA starts by itself after every shutdown/sign-in. Settings saved by older versions get the new default once;
            // afterwards the checkbox in Settings is respected. Registering on every launch also repairs a moved .exe.
            if (!_settings.StartupDefaultApplied) { _settings.StartWithWindows = true; _settings.StartupDefaultApplied = true; _store.Save(_settings); }
            StartupService.SetEnabled(_settings.StartWithWindows);
        }
        FileLog.Write($"Start: safe={_safeMode} notch={_settings.NotchMode} hideTaskbar={_settings.HideTaskbar} runningApps={_settings.ShowRunningApps} reserve={_settings.DockReservesSpace} media={_settings.ShowMedia}");

        // Started by Windows at sign-in (--startup): Explorer may not be ready yet, and the taskbar can only be hidden and the
        // screen space reserved once it is. So wait for it (at most 30 s) instead of racing it.
        bool atSignIn = !_safeMode && e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        if (atSignIn) WaitForShell(() => StartShell(load));
        else StartShell(load);
    }

    void WaitForShell(Action then)
    {
        int ticks = 0;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        t.Tick += (_, _) =>
        {
            if (++ticks < 60 && !NativeMethods.ShellIsReady()) return;
            t.Stop();
            var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };   // let the taskbar finish drawing
            settle.Tick += (_, _) => { settle.Stop(); then(); };
            settle.Start();
        };
        t.Start();
    }

    void StartShell(LoadResult load)
    {
        var system = _system = new SystemChangesService();
        Guard("system", () => system.Apply(_settings));   // taskbar auto-hide / dark mode, only if switched on; restored on exit
        Guard("taskbar-guard", () =>
        {
            // Explorer re-shows the taskbar after a restart, a display change or a hover on the screen edge: keep it hidden.
            _taskbarGuard.Tick += (_, _) => { if (_settings.HideTaskbar && !_safeMode) _system?.KeepTaskbarHidden(); };
            _taskbarGuard.Start();
        });

        ThemeService.Apply(this, _settings);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        bool opaque = _settings.OpaqueFallback;   // safe mode keeps the normal see-through windows; only this setting makes them solid

        // Each feature is isolated: a failure in one must not take down the others.
        Guard("dock", () =>
        {
            _dock = new DockWindow(_settings, opaque, Persist, Notify);
            _dock.Show();
        });
        Guard("topbar", () => ApplyNotch(_settings));   // must exist before the Island so the notch ends up above it
        Guard("island", () =>
        {
            _island = new IslandWindow(_settings, opaque, ResetLayout, () => Shutdown(), () => _dock?.FocusFirst(), OpenSettings, OpenClipboard);
            _island.StatusProvider = _status.Sample;
            var metrics = new SystemStatusService();   // its own readings, so it does not disturb the status line's CPU baseline
            _island.MetricsProvider = metrics.SampleMetrics;
            _island.SettingsEdited += () => Persist(_settings);   // a switch on the drink / prayer page was flipped
            _island.MediaPlayPause += () => _media?.TogglePlayPause();
            _island.MediaNext += () => _media?.Next();
            _island.MediaPrevious += () => _media?.Previous();
            _island.EnableLyricsRequested += () =>
            {
                var c = _settings.Clone();
                c.LyricsEnabled = true;   // the user tapped "Aktifkan lirik": consent for artist + title to go to lrclib.net
                ApplySettings(c, keepCurrentDock: true);
            };
            _island.EnableClipboardRequested = () =>
            {
                var c = _settings.Clone();
                c.ClipboardHistoryEnabled = true;   // the user tapped "Aktifkan riwayat clipboard", which sits next to the privacy note
                ApplySettings(c, keepCurrentDock: true);
            };
            _island.OpacityChanged += v => { _settings.IslandOpacity = v; Persist(_settings); };
            _island.PositionChanged += (x, y) =>
            {
                // The Island has already moved itself; remember where it is (the same two numbers Settings shows).
                _settings.IslandOffsetX = x;
                _settings.IslandTopOffset = y;
                Persist(_settings);
                _topReserve?.Apply(_settings.IslandMonitor, NotchLayout.IslandStripDip(_settings.ClockFontSize, Math.Min(_settings.IslandTopOffset, 40)));
            };
            _island.EnsureVisibilityForMode();
        });
        Guard("hotkey", () =>
        {
            if (_island is null) return;
            _hotkey = new HotkeyService(_island, () => _island.Invoke());
            if (!_hotkey.Register(_settings.Hotkey))
                Notify($"Shortcut {_settings.Hotkey} is unavailable. Use the tray icon, or change it in Settings.", true);
            for (int i = 0; i < 7; i++)
            {
                int page = i;
                _pageHotkeys.Add(new HotkeyService(_island, () => _island.InvokePage(page), 0x4E51 + i));
            }
            if (!RegisterPageHotkeys(_settings))
                Notify($"Some page shortcuts ({_settings.PageHotkeyPrefix}+1…7) are in use by another app. Change the prefix in Settings.", true);
            _toggleHotkey = new HotkeyService(_island, () => _island.ToggleHidden(), 0x4E58);
            if (!RegisterToggleHotkey(_settings))
                Notify($"Shortcut {_settings.ToggleHotkey} (hide / show the Island) is in use by another app. Change it in Settings.", true);
            _translateHotkey = new HotkeyService(_island, () => _island.InvokeTranslate(), 0x4E5B);
            if (!RegisterTranslateHotkey(_settings))
                Notify($"Shortcut {_settings.TranslateHotkey} (translate in the notch) is in use by another app. Change it in Settings.", true);
            _askHotkey = new HotkeyService(_island, () => _island.InvokeAsk(), 0x4E5A);
            if (!RegisterAskHotkey(_settings))
                Notify($"Shortcut {_settings.AskHotkey} (ask / search in the notch) is in use by another app. Change it in Settings.", true);
            _launcherHotkey = new HotkeyService(_island, OpenLauncher, 0x4E59);
            if (!RegisterLauncherHotkey(_settings))
                Notify($"Shortcut {_settings.LauncherHotkey} (quick launcher) is in use by another app. Change it in Settings.", true);
        });
        Guard("tray", () => _tray = new TrayService(
            () => _island?.Invoke(), () => _dock?.FocusFirst(), OpenClipboard, OpenSettings, ResetLayout, () => Shutdown(),
            () => _island?.ToggleHidden(), OpenLauncher));
        Guard("media", () => ApplyMedia(_settings));
        Guard("clipboard", () => ApplyClipboard(_settings));
        Guard("weather", () =>
        {
            _weather = new WeatherService();
            _weather.TextChanged += t => { _island?.SetWeather(t); _topBar?.SetWeather(t); };
            _weather.Configure(_settings);
            StartLocation();
        });

        Guard("activity", () => ApplyActivity(_settings));
        Guard("alerts", () => ApplyAlerts(_settings));
        Guard("harmony", () => ApplyHarmony(_settings));
        Guard("shape", () => ApplyShape(_settings));
        Guard("eco", () => ApplyEco(_settings));

        if (_settings.OpaqueFallback && !_safeMode) Notify("Solid windows are on (Settings → Use opaque windows). Turn it off and restart NOVA for the see-through Island, the rounded dock and icon zoom.", true);
        if (_safeMode) Notify("Safe mode: clock and dock only, default layout, nothing is saved.", true);
        if (load.Warning is not null) Notify("Your settings file was unreadable, so defaults were restored.", true);
    }

    void Notify(string text, bool important) => _island?.ShowNotice(text, important);

    // ---- live activities: short messages on the Island when something happens ----

    AudioService.Watcher? _audioWatcher;
    readonly System.Windows.Threading.DispatcherTimer _volumeTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    double _volumeNow; bool _mutedNow;
    QuickStatus? _lastQuick;
    bool? _lastCharging; bool _warned20, _warned10, _powerHooked;

    void Activity(string text, string? glyph = null, string? color = null)
    { if (_settings.ActivityAlerts) _island?.ShowNotice(text, important: true, seconds: 4, glyph: glyph, badgeColor: color); }

    void ApplyActivity(NovaSettings s)
    {
        if (s.ActivityAlerts && !_safeMode)
        {
            if (_audioWatcher is null)
            {
                _volumeTimer.Tick -= OnVolumeTimer; _volumeTimer.Tick += OnVolumeTimer;
                var w = new AudioService.Watcher();
                if (w.Start((v, m) => Dispatcher.InvokeAsync(() => { _volumeNow = v; _mutedNow = m; _volumeTimer.Stop(); _volumeTimer.Start(); })))
                    _audioWatcher = w;
                else w.Dispose();
            }
            if (!_powerHooked) { Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged; _powerHooked = true; }
        }
        else StopActivity();
    }

    void StopActivity()
    {
        _volumeTimer.Stop();
        _audioWatcher?.Dispose(); _audioWatcher = null;
        if (_powerHooked) { Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged; _powerHooked = false; }
    }

    void OnVolumeTimer(object? sender, EventArgs e)
    {
        _volumeTimer.Stop();
        if (_island is null || _island.IsExpanded) return;   // the Island's own slider already shows it
        Activity(_mutedNow ? "Suara dibisukan" : $"Volume {Math.Round(_volumeNow * 100)}%", _mutedNow ? "\uE74F" : "\uE767", "#4C8DFF");
    }

    void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.StatusChange) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (!NativeMethods.GetSystemPowerStatus(out var p)) return;
            var info = Nova.Core.Status.BatteryInfo.From(p.ACLineStatus, p.BatteryFlag, p.BatteryLifePercent);
            if (info is null) return;   // desktop PC: no battery
            if (_lastCharging is bool was && was != info.Charging)
                Activity(info.Charging ? $"Mengisi daya · {info.Percent}%" : $"Memakai baterai · {info.Percent}%", "\uE83E", info.Charging ? "#30A46C" : "#4C8DFF");
            _lastCharging = info.Charging;
            if (info.Charging) { _warned20 = _warned10 = false; return; }
            if (info.Percent <= 10 && !_warned10) { _warned10 = true; Activity($"Baterai hampir habis · {info.Percent}%", "\uE850", "#E5484D"); }
            else if (info.Percent <= 20 && !_warned20) { _warned20 = true; Activity($"Baterai rendah · {info.Percent}%", "\uE850", "#F5A524"); }
        });
    }

    /// <summary>Compares each status reading with the previous one and announces what changed.</summary>
    void OnQuickStatus(QuickStatus q)
    {
        var prev = _lastQuick;
        _lastQuick = q;
        if (prev is null || !_settings.ActivityAlerts) return;

        if (prev.Net == NetKind.None && q.Net != NetKind.None)
            Activity(q.Net == NetKind.Wifi ? $"Wi-Fi terhubung{(q.WifiName is null ? "" : " · " + q.WifiName)}" : "Jaringan kabel terhubung", "\uE701", "#4C8DFF");
        else if (prev.Net != NetKind.None && q.Net == NetKind.None) Activity("Jaringan terputus", "\uEB55", "#E5484D");

        var before = prev.BluetoothNames ?? Array.Empty<string>();
        var after = q.BluetoothNames ?? Array.Empty<string>();
        var joined = after.FirstOrDefault(n => !before.Contains(n));
        var left = before.FirstOrDefault(n => !after.Contains(n));
        if (joined is not null) Activity("Bluetooth terhubung · " + joined, "\uE702", "#4C8DFF");
        else if (left is not null) Activity("Bluetooth terputus · " + left, "\uE702", "#8A8A94");
    }

    // ---- chat alerts (WhatsApp, Telegram...) ----

    void ApplyAlerts(NovaSettings s)
    {
        if (s.AppAlerts && !_safeMode)
        {
            if (_alerts is not null) return;
            var alerts = _alerts = new AppAlertService();
            alerts.Alert += (app, unread, window) => Dispatcher.InvokeAsync(() =>
                _island?.ShowNotice(AppAlerts.Describe(app, unread), important: true, seconds: 6, glyph: "\uE8BD", badgeColor: app.Accent,
                                    onClick: () => NativeMethods.Activate(window)));
            alerts.Flashed += (window, path) => Dispatcher.InvokeAsync(() => _dock?.Bounce(window, path));
            alerts.Start();
        }
        else if (_alerts is not null) { _alerts.Dispose(); _alerts = null; }
    }

    // ---- one look for every window ----

    bool ThemeIsDark()
    {
        if (Resources["NovaWindow"] is System.Windows.Media.SolidColorBrush b)
        {
            var c = b.Color;
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B < 128;
        }
        return true;
    }

    void ApplyHarmony(NovaSettings s)
    {
        bool on = s.HarmonizeWindows && !_safeMode && !SystemParameters.HighContrast && !s.HighContrastOverride;
        if (on) (_harmony ??= new WindowHarmonyService()).Apply(true, ThemeIsDark());
        else _harmony?.Apply(false, false);
    }

    /// <summary>Large rounded corners ("rounded square") on other programs' windows. Off in safe mode and in high contrast.</summary>
    void ApplyShape(NovaSettings s)
    {
        bool on = s.RoundAppWindows && !_safeMode && !SystemParameters.HighContrast && !s.HighContrastOverride;
        if (on) (_shape ??= new WindowShapeService()).Apply(true, s.WindowCornerRadius);
        else _shape?.Apply(false, 0);
    }

    // ---- status polling when there is no top bar to do it ----

    readonly System.Windows.Threading.DispatcherTimer _statusPoll = new() { Interval = TimeSpan.FromSeconds(5) };
    bool _statusBusy, _statusHooked;

    void UpdateStatusPoll(NovaSettings s, bool barShowing)
    {
        if (!_statusHooked)
        {
            _statusHooked = true;
            _statusPoll.Tick += async (_, _) =>
            {
                if (_statusBusy) return;
                _statusBusy = true;
                try { OnQuickStatus(await Task.Run(QuickStatusService.ReadAsync)); }
                catch (Exception ex) { FileLog.Write($"Status poll failed: {ex.GetType().Name}"); }
                finally { _statusBusy = false; }
            };
        }
        bool need = s.ShowSystemStatus && s.ActivityAlerts && !barShowing && !_safeMode;
        if (need) { if (!_statusPoll.IsEnabled) _statusPoll.Start(); } else _statusPoll.Stop();
    }

    // ---- light on resources: low priority, efficiency mode, and giving idle memory back ----

    readonly System.Windows.Threading.DispatcherTimer _trimTimer = new() { Interval = TimeSpan.FromMinutes(3) };
    bool _trimHooked;

    void ApplyEco(NovaSettings s)
    {
        NativeMethods.EnableEcoQoS(s.EcoMode);
        try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = s.EcoMode ? System.Diagnostics.ProcessPriorityClass.BelowNormal : System.Diagnostics.ProcessPriorityClass.Normal; }
        catch { /* not allowed: harmless */ }

        if (!_trimHooked)
        {
            _trimHooked = true;
            _trimTimer.Tick += (_, _) =>
            {
                if (!_settings.EcoMode || (_island?.IsExpanded ?? false)) return;   // never while the user is using the Island
                GC.Collect(2, GCCollectionMode.Optimized, blocking: true, compacting: false);
                NativeMethods.TrimWorkingSet();
            };
        }
        if (s.EcoMode) { if (!_trimTimer.IsEnabled) _trimTimer.Start(); }
        else _trimTimer.Stop();
    }

    /// <summary>Prefix + 1…7 → page 1…7 of the Island. Returns false if any of them is taken by another app (an empty prefix switches them all off and counts as fine).</summary>
    bool RegisterPageHotkeys(NovaSettings s)
    {
        bool all = true;
        for (int i = 0; i < _pageHotkeys.Count; i++)
        {
            if (s.PageHotkeyPrefix.Length == 0) { _pageHotkeys[i].Register(""); continue; }
            all &= _pageHotkeys[i].Register($"{s.PageHotkeyPrefix}+{i + 1}");
        }
        return all;
    }

    /// <summary>Registers the hide/show shortcut. An empty shortcut switches it off and counts as fine.</summary>
    bool RegisterToggleHotkey(NovaSettings s)
    {
        if (_toggleHotkey is null) return true;
        if (s.ToggleHotkey.Length == 0) { _toggleHotkey.Register(""); return true; }
        return _toggleHotkey.Register(s.ToggleHotkey);
    }

    /// <summary>Registers the quick launcher shortcut. An empty shortcut switches it off and counts as fine.</summary>
    bool RegisterLauncherHotkey(NovaSettings s)
    {
        if (_launcherHotkey is null) return true;
        if (s.LauncherHotkey.Length == 0) { _launcherHotkey.Register(""); return true; }
        return _launcherHotkey.Register(s.LauncherHotkey);
    }

    bool RegisterTranslateHotkey(NovaSettings s)
    {
        if (_translateHotkey is null) return true;
        if (s.TranslateHotkey.Length == 0) { _translateHotkey.Register(""); return true; }
        return _translateHotkey.Register(s.TranslateHotkey);
    }

    bool RegisterAskHotkey(NovaSettings s)
    {
        if (_askHotkey is null) return true;
        if (s.AskHotkey.Length == 0) { _askHotkey.Register(""); return true; }
        return _askHotkey.Register(s.AskHotkey);
    }

    /// <summary>Opens the quick launcher, or closes it if it is already open.</summary>
    void OpenLauncher()
    {
        if (_launcher is not null) { _launcher.Close(); return; }
        _launcher = new LauncherWindow(_settings.DockItems, Notify);
        _launcher.Closed += (_, _) => _launcher = null;
        _launcher.Show();
    }

    void Persist(NovaSettings s) { if (!_safeMode) _store.Save(s); }

    // ---- applying settings ----

    /// <summary>Applies, persists, and re-registers everything. Returns false if the hotkey could not be registered.</summary>
    bool ApplySettings(NovaSettings incoming, bool keepCurrentDock)
    {
        var old = _settings;
        var s = SettingsValidator.Normalize(incoming);
        if (keepCurrentDock) s.DockItems = old.DockItems;   // the dock may have changed while Settings was open


        _settings = s;
        ThemeService.Apply(this, s);
        ApplyNotch(s);
        _island?.ApplySettings(s);
        _dock?.ApplySettings(s);
        _system?.Apply(s);
        var hotkeyOk = _hotkey?.Register(s.Hotkey) ?? true;
        hotkeyOk &= RegisterPageHotkeys(s);
        hotkeyOk &= RegisterToggleHotkey(s);
        hotkeyOk &= RegisterLauncherHotkey(s);
        hotkeyOk &= RegisterAskHotkey(s);
        hotkeyOk &= RegisterTranslateHotkey(s);

        ApplyMedia(s);
        ApplyClipboard(s);
        ApplyActivity(s);
        ApplyAlerts(s);
        ApplyHarmony(s);
        ApplyShape(s);
        ApplyEco(s);
        if (!s.LyricsEnabled) { _lyricsCts?.Cancel(); _island?.SetLyrics(null); }
        else if (!old.LyricsEnabled && _lyricsTrack is { HasTrack: true } t) _ = LoadLyricsAsync(t);
        _weather?.Configure(s);
        if (s.WeatherEnabled && double.IsNaN(s.WeatherLatitude)) _ = RefreshLocationAsync();   // switched on: find where this laptop is

        if (!_safeMode)
        {
            StartupService.SetEnabled(s.StartWithWindows);
            _store.Save(s);
        }
        return hotkeyOk;
    }

    /// <summary>
    /// Top of the screen. Full-bar mode: a bar across the screen with Wi-Fi / sound / battery icons. Island-only mode (default):
    /// no bar and no icons, just the Island, plus an invisible strip that keeps windows below it.
    /// </summary>
    void ApplyNotch(NovaSettings s)
    {
        bool bar = s.NotchMode && s.ShowTopBar;
        bool strip = s.NotchMode && !s.ShowTopBar && s.ReserveTopSpace;

        if (bar)
        {
            if (_topBar is null)
            {
                _topBar = new TopBarWindow(s);
                _topBar.Repositioned += () => _island?.BringToFront();
                _topBar.StatusRead += OnQuickStatus;
                _topBar.RadioRequested += kind => _island?.ShowRadio(kind);
                _topBar.FullscreenChanged += full => { _island?.SetFullscreen(full); _dock?.SetFullscreen(full); };
                _topBar.Show();
            }
            _topBar.ApplySettings(s);
        }
        else if (_topBar is not null)
        {
            var old = _topBar;
            _topBar = null;
            old.Release();   // give the reserved space back immediately
            old.Close();
        }

        if (strip)
        {
            if (_topReserve is null)
            {
                _topReserve = new DockReserveWindow(bottom: false);
                _topReserve.Repositioned += () => _island?.BringToFront();
                _topReserve.FullscreenChanged += full => { _island?.SetFullscreen(full); _dock?.SetFullscreen(full); };
            }
            _topReserve.Apply(s.IslandMonitor, NotchLayout.IslandStripDip(s.ClockFontSize, Math.Min(s.IslandTopOffset, 40)));   // a dragged-down Island must not squeeze the windows
        }
        else if (_topReserve is not null)
        {
            var old = _topReserve;
            _topReserve = null;
            old.Release();
            old.Close();
        }

        UpdateStatusPoll(s, bar);
    }

    void ResetLayout()
    {
        ApplySettings(new NovaSettings(), keepCurrentDock: false);
        Notify("Layout reset to defaults.", true);
    }

    // ---- integrations ----

    void ApplyMedia(NovaSettings s)
    {
        if (s.ShowMedia && _media is null)
        {
            _media = new MediaService(Dispatcher);
            _media.Changed += OnMediaChanged;
            _media.ArtChanged += bytes => _island?.SetArt(bytes);
            _media.TimelineChanged += t => _island?.SetTimeline(t);
            _ = _media.StartAsync();
        }
        else if (!s.ShowMedia && _media is not null)
        {
            _media.Changed -= OnMediaChanged;
            _media.Dispose();
            _media = null;
            _lastTrack = null; _lyricsTrack = null;
            _island?.SetMedia(null); _island?.SetArt(null); _island?.SetTimeline(null); _island?.SetLyrics(null);
        }
    }

    void OnMediaChanged(MediaInfo? info)
    {
        _island?.SetMedia(info);
        if (info is { HasTrack: true, IsPlaying: true } && info.IsDifferentTrack(_lastTrack) && _settings.IslandBehavior == IslandBehavior.Adaptive)
            _island?.ShowNotice("Now playing: " + info.Describe());   // informational: honours "temporary notices"
        if (info is { HasTrack: true }) _lastTrack = info;

        _island?.SetMediaSource(info is { HasTrack: true } ? _media?.SourceApp : null);

        // New song: forget the old lyrics, and look up the new ones only if the user opted in.
        if (info is { HasTrack: true })
        {
            if (_lyricsTrack is null || info.IsDifferentTrack(_lyricsTrack))
            {
                _lyricsTrack = info;
                _island?.SetLyrics(null);
                if (_settings.LyricsEnabled) _ = LoadLyricsAsync(info);
            }
        }
        else if (_lyricsTrack is not null) { _lyricsTrack = null; _lyricsCts?.Cancel(); _island?.SetLyrics(null); }
    }

    async Task LoadLyricsAsync(MediaInfo track)
    {
        _lyricsCts?.Cancel();
        var cts = _lyricsCts = new CancellationTokenSource();
        var length = _media?.Timeline?.Duration;
        var lines = await _lyricsService.FetchAsync(track.Artist, track.Title, length, cts.Token);
        if (cts.IsCancellationRequested || !_settings.LyricsEnabled) return;
        if (_lyricsTrack is null || _lyricsTrack.IsDifferentTrack(track)) return;   // the song changed while we were looking
        _island?.SetLyrics(lines);
    }

    void ApplyClipboard(NovaSettings s)
    {
        if (s.ClipboardHistoryEnabled)
        {
            if (_clipboard is null && _island is not null)
            {
                _clipboard = new ClipboardMonitor(_island, s.ClipboardMaxItems);
                if (!_clipboard.Start()) { FileLog.Write("Clipboard listener could not be registered."); _clipboard.Dispose(); _clipboard = null; Notify("Clipboard history couldn't start.", true); }
                else _island.ClipboardSource = _clipboard.History;
            }
            else if (_clipboard is not null) _clipboard.History.MaxItems = s.ClipboardMaxItems;
        }
        else if (_clipboard is not null)
        {
            _clipboardWindow?.Close();
            if (_island is not null) _island.ClipboardSource = null;
            _clipboard.Dispose();   // wipes the history
            _clipboard = null;
        }
    }

    void OpenClipboard()
    {
        if (_clipboard is null) { Notify("Clipboard history is off. Turn it on in Settings.", true); return; }
        if (_clipboardWindow is not null) { _clipboardWindow.Activate(); return; }
        _clipboardWindow = new ClipboardWindow(_clipboard.History, Notify);
        _clipboardWindow.Closed += (_, _) => _clipboardWindow = null;
        _clipboardWindow.Show();
    }

    // ---- location: the laptop's GPS drives the weather and the prayer times ----

    readonly System.Windows.Threading.DispatcherTimer _locationTimer = new() { Interval = TimeSpan.FromMinutes(30) };
    bool _locationBusy, _locationWarned;

    void StartLocation()
    {
        if (_safeMode) return;
        _locationTimer.Interval = TimeSpan.FromMinutes(60);   // a laptop rarely changes city within the hour: fewer wake-ups
        _locationTimer.Tick += (_, _) => _ = RefreshLocationAsync();
        _locationTimer.Start();
        _ = RefreshLocationAsync();
    }

    async Task RefreshLocationAsync()
    {
        if (_locationBusy || _safeMode) return;
        _locationBusy = true;
        try
        {
            var fix = await LocationService.GetAsync();
            if (fix.Status != LocationService.Status.Ok)
            {
                // Keep the last known position (or Jakarta before the first fix); tell the person once how to fix it.
                if (!_locationWarned)
                {
                    _locationWarned = true;
                    Notify(fix.Status == LocationService.Status.Denied
                        ? "Lokasi belum diizinkan. Nyalakan di Pengaturan > Privasi & keamanan > Lokasi (juga untuk aplikasi desktop)."
                        : "Posisi laptop belum bisa dibaca. Waktu sholat memakai lokasi terakhir.", true);
                }
                return;
            }
            _locationWarned = false;
            var s = _settings;
            bool moved = double.IsNaN(s.WeatherLatitude) || Math.Abs(s.PrayerLatitude - fix.Latitude) > 0.01 || Math.Abs(s.PrayerLongitude - fix.Longitude) > 0.01;
            s.PrayerCity = PrayerPlaces.Custom;
            s.PrayerLatitude = fix.Latitude; s.PrayerLongitude = fix.Longitude;
            s.WeatherCity = "GPS";   // marker: the weather position comes from the GPS (no city to type)
            s.WeatherLatitude = fix.Latitude; s.WeatherLongitude = fix.Longitude;
            // Only ask for the place name when the person moved (or none is known yet): at most once per move.
            if (moved || string.IsNullOrEmpty(s.PlaceName))
            {
                var name = await LocationService.PlaceNameAsync(fix.Latitude, fix.Longitude);
                if (name is not null && name != s.PlaceName) { s.PlaceName = name; moved = true; }
            }
            s.WeatherPlace = s.PlaceName;
            if (moved)
            {
                Persist(s);
                _island?.ApplySettings(s);   // the prayer page reads the new position
                _weather?.Configure(s);
            }
        }
        catch (Exception ex) { FileLog.Write($"Location refresh failed: {ex.GetType().Name}"); }
        finally { _locationBusy = false; }
    }

    // ---- windows ----

    void OpenSettings()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(
            _settings,
            apply: (s, replaceDock) => ApplySettings(s, keepCurrentDock: !replaceDock),
            resetAll: () => { ApplySettings(new NovaSettings(), keepCurrentDock: false); return _settings; });
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        => Dispatcher.InvokeAsync(() =>
        {
            ThemeService.Apply(this, _settings);   // theme / high-contrast changes
            _island?.RefreshTheme();
            _dock?.RefreshTheme();
            ApplyHarmony(_settings);
            ApplyShape(_settings);
            _system?.KeepTaskbarHidden();
            _island?.Relocate();                   // the work area changes when the taskbar auto-hides
            _dock?.Relocate();
        });

    void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.InvokeAsync(() => { _system?.KeepTaskbarHidden(); _topBar?.Relocate(); _topReserve?.Relocate(); _island?.Relocate(); _dock?.Relocate(); });   // monitor plugged/unplugged, resolution or scale changed

    static void Guard(string feature, Action a)
    {
        try { a(); } catch (Exception ex) { FileLog.Write($"Feature '{feature}' failed to start: {ex.GetType().Name}: {ex.Message}"); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbarGuard.Stop();
        StopActivity();
        _trimTimer.Stop();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _tray?.Dispose();
        _hotkey?.Dispose();
        foreach (var h in _pageHotkeys) h.Dispose();
        _toggleHotkey?.Dispose();
        _launcherHotkey?.Dispose();
        _askHotkey?.Dispose();
        _translateHotkey?.Dispose();
        _launcher?.Close();
        _statusPoll.Stop();
        _alerts?.Dispose();
        _harmony?.Dispose();   // puts every window's title bar back as it was
        _shape?.Dispose();     // removes the rounded shapes from every window
        _topBar?.Release();
        _topReserve?.Release();
        _dock?.ReleaseSpace();
        _system?.RestoreAll();
        _clipboard?.Dispose();
        _media?.Dispose();
        _locationTimer.Stop();
        SystemToggles.RestoreTint();
        _weather?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
