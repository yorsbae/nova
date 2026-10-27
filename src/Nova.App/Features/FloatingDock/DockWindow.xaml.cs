using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Nova.App.Integrations.Windows;
using Nova.Core.Configuration;
using Nova.Core.Dock;
using Nova.Core.Launch;
using Nova.Core.Logging;

namespace Nova.App.Features.FloatingDock;

public partial class DockWindow : Window
{
    readonly Action<NovaSettings> _persist;
    readonly Action<string, bool> _notify;
    readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    readonly RunningAppsService _running = new();
    readonly DispatcherTimer _runTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    readonly DockReserveWindow _reserve = new();
    readonly WindowGenie _genie = new();
    readonly List<Tile> _tiles = new();
    double _restPanelHeight = 40, _baseItemsWidth, _sigma = 40, _icon = 26;
    bool _magHooked;
    IReadOnlyList<RunningApp> _apps = Array.Empty<RunningApp>();
    string _appsKey = "";
    NovaSettings _s;
    bool _revealed = true, _modal, _fullscreen;
    readonly HashSet<string> _knownTiles = new();
    bool _firstBuild = true;

    public DockWindow(NovaSettings settings, bool opaque, Action<NovaSettings> persist, Action<string, bool> notify)
    {
        _s = settings; _persist = persist; _notify = notify;
        InitializeComponent();
        OverlayChrome.Configure(this, opaque);
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); TryHide(); };
        _runTimer.Tick += (_, _) => PollRunning();
        Panel.MouseLeave += (_, _) => { if (_pendingRefresh) Dispatcher.InvokeAsync(() => RefreshRunning(force: false), DispatcherPriority.Background); };
        _reserve.FullscreenChanged += SetFullscreen;
        _reserve.Repositioned += () => { if (!_placing) Reposition(); };
        Closed += (_, _) => { _hideTimer.Stop(); _runTimer.Stop(); StopZoomLoop(); _reserve.Close(); };
        // The zoom follows the pointer over the dock; the loop also runs for a moment after it leaves, to shrink the icons back.
        Panel.MouseEnter += (_, _) => StartZoomLoop();
        Panel.MouseMove += OnPanelMouseMove;
        Panel.MouseLeave += (_, _) => StartZoomLoop();

        // Right-click on the dock itself (not on an icon): add an app. There is no permanent "+" button taking up room.
        var menu = new ContextMenu();
        var add = new MenuItem { Header = "Add app…" };
        add.Click += (_, _) => Add();
        menu.Items.Add(add);
        var taskMgr = new MenuItem { Header = "Task Manager" };
        taskMgr.Click += (_, _) => AppControl.OpenTaskManager();
        menu.Items.Add(taskMgr);
        menu.Opened += (_, _) => _modal = true;
        menu.Closed += (_, _) => { _modal = false; if (RevealMode) _hideTimer.Start(); };
        AutoCloseWhenPointerLeaves(menu, Panel);
        Panel.ContextMenu = menu;

        ApplySettings(settings);
    }

    /// <summary>
    /// A right-click menu closes by itself when the pointer has been away from both the menu and the icon it belongs to for a moment,
    /// so it does not stay open (and keep the dock frozen) when the user simply moves on.
    /// </summary>
    void AutoCloseWhenPointerLeaves(ContextMenu menu, FrameworkElement owner)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        int outside = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                if (!menu.IsOpen) { timer.Stop(); return; }
                if (!NativeMethods.TryGetCursor(out int mx, out int my)) return;
                double dpi = VisualTreeHelper.GetDpi(menu).DpiScaleX; if (dpi <= 0) dpi = 1;
                const double margin = 14;
                bool Inside(FrameworkElement el)
                {
                    if (el.ActualWidth <= 0 || !el.IsVisible) return false;
                    var tl = el.PointToScreen(new Point(0, 0));
                    return mx >= tl.X - margin && mx <= tl.X + el.ActualWidth * dpi + margin &&
                           my >= tl.Y - margin && my <= tl.Y + el.ActualHeight * dpi + margin;
                }
                if (Inside(menu) || Inside(owner)) { outside = 0; return; }
                if (++outside >= 4) { outside = 0; timer.Stop(); menu.IsOpen = false; }   // about half a second away
            }
            catch (InvalidOperationException) { timer.Stop(); }
        };
        menu.Opened += (_, _) => { outside = 0; timer.Start(); };
        menu.Closed += (_, _) => timer.Stop();
    }

    /// <summary>One icon in the dock, with what the zoom animation needs to know about it.</summary>
    sealed class Tile
    {
        public Button Button = null!;
        public ScaleTransform Zoom = new(1, 1);
        public double BaseCenter, Scale = 1;
        public IntPtr Hwnd;
        public string? Exe;
    }

    /// <summary>The transparent window is as wide as the screen so icons can grow without moving it. The opaque fallback keeps the old snug size.</summary>
    bool Wide => AllowsTransparency;
    bool MotionOff => _s.ReducedMotion || !SystemParameters.ClientAreaAnimation;
    bool MagnifyOn => Wide && _s.DockMagnify && _s.DockMagnifyScale > 1.01 && !MotionOff;
    double Headroom => MagnifyOn ? DockMagnify.HeadroomFor(_icon, _s.DockMagnifyScale) : 6;

    bool RevealMode => _s.DockVisibility == DockVisibility.RevealOnEdgeHover;
    bool ReserveSpace => _s.DockReservesSpace && !RevealMode;

    public void ApplySettings(NovaSettings s)
    {
        _s = s;
        Panel.Margin = new Thickness(0, 0, 0, s.DockBottomOffset);
        Root.Margin = new Thickness(0);
        // The old snug opaque window needs its whole area hit-testable in reveal mode. The wide transparent window must NOT be:
        // it spans the screen, and clicks on its see-through parts have to reach the windows below.
        Root.Background = RevealMode && !Wide ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : null;
        if (s.ShowRunningApps)
        {
            _apps = _running.Snapshot();
            _appsKey = AppsKey(_apps);
            _runTimer.Start();
        }
        else { _runTimer.Stop(); _apps = Array.Empty<RunningApp>(); _appsKey = ""; }
        ApplyGlass();
        if (!MagnifyOn) ResetZoom();
        Rebuild();
        SetRevealed(!RevealMode);
        Reposition();
    }

    /// <summary>Re-reads theme colors (Windows light/dark or high-contrast change).</summary>
    public void RefreshTheme() => ApplyGlass();

    /// <summary>Glass look shared with the Island: a rounded dark glass panel with a thin light edge (high contrast uses solid system colors).</summary>
    void ApplyGlass()
    {
        bool highContrast = SystemParameters.HighContrast || _s.HighContrastOverride;
        if (_s.DockGlass && AllowsTransparency && !highContrast)
        {
            Panel.CornerRadius = new CornerRadius(Nova.App.Themes.GlassLook.Radius);
            Panel.Background = Nova.App.Themes.GlassLook.Fill((byte)Math.Round(255 * Math.Clamp(_s.DockOpacity, 0.2, 1.0)));
            Panel.BorderBrush = Nova.App.Themes.GlassLook.Edge();
        }
        else
        {
            Panel.SetResourceReference(Border.BackgroundProperty, "NovaSurface");
            Panel.SetResourceReference(Border.BorderBrushProperty, "NovaBorder");
        }
    }

    /// <summary>A full-screen app or game started or stopped: the dock steps aside so it never covers it.</summary>
    public void SetFullscreen(bool fullscreen)
    {
        if (_fullscreen == fullscreen) return;
        _fullscreen = fullscreen;
        if (fullscreen) Hide();
        else if (!IsVisible) { Show(); Reposition(); }
    }

    /// <summary>Gives the screen space back to Windows immediately (used on exit).</summary>
    public void ReleaseSpace() => _reserve.Release();

    /// <summary>Explicit keyboard entry point (from the Island or tray). Only then does the dock take focus.</summary>
    public void FocusFirst()
    {
        if (!IsVisible) Show();
        SetRevealed(true);
        NativeMethods.SetOverlayStyle(this, noActivate: false);
        Activate();
        UpdateLayout();
        Items.Children.OfType<Button>().FirstOrDefault()?.Focus();
    }

    void SetRevealed(bool revealed)
    {
        _revealed = revealed || !RevealMode;
        if (!_revealed) ResetZoom();
        Panel.Visibility = _revealed ? Visibility.Visible : Visibility.Collapsed;
        Handle.Visibility = !_revealed ? Visibility.Visible : Visibility.Collapsed;
        Reposition();
    }

    void TryHide()
    {
        if (!RevealMode || _modal || IsKeyboardFocusWithin || IsMouseOver) return;
        SetRevealed(false);
    }

    static string AppsKey(IEnumerable<RunningApp> apps) => string.Join("|", apps.Select(a => a.Hwnd.ToInt64()));

    bool _pendingRefresh;
    DockItem? _dragItem;                                  // the pinned app being dragged to a new place
    readonly Dictionary<Button, DockItem> _pinnedButtons = new();

    /// <summary>Checks which apps are open (every 4 s, only while the dock is enabled) and redraws only if that changed.</summary>
    void PollRunning() => RefreshRunning(force: false);

    /// <summary>
    /// Reads the open apps and redraws when they changed. A normal refresh waits while the pointer or keyboard is on the dock (the buttons must not
    /// move under it), but remembers that it is due and runs when the pointer leaves; <paramref name="force"/> is used right after a click
    /// (launch, close), where the dock must show the result at once. A forced refresh still never rebuilds under an open menu or dialog.
    /// </summary>
    void RefreshRunning(bool force)
    {
        if (_modal || _fullscreen || _dragItem is not null) return;
        var snap = _running.Snapshot();
        var key = AppsKey(snap);
        if (key == _appsKey) { _pendingRefresh = false; return; }
        if (!force && (IsKeyboardFocusWithin || Panel.IsMouseOver)) { _pendingRefresh = true; return; }
        _pendingRefresh = false;
        _apps = snap; _appsKey = key;
        Rebuild();
    }

    /// <summary>A program that is starting needs a moment before its window exists, so look again a few times instead of waiting for the slow poll.</summary>
    void RefreshSoon()
    {
        foreach (var ms in new[] { 500, 1300, 2600, 5000 })
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (_, _) => { t.Stop(); RefreshRunning(force: true); };
            t.Start();
        }
    }

    static bool Matches(DockItem item, RunningApp app)
    {
        if (app.ExePath is not { } exe) return false;
        var target = ShortcutResolver.ExeOf(item.Path);   // a pinned .lnk is matched by the program it starts
        string appName = System.IO.Path.GetFileNameWithoutExtension(exe);
        return string.Equals(exe, item.Path, StringComparison.OrdinalIgnoreCase) ||
               (target is not null && string.Equals(exe, target, StringComparison.OrdinalIgnoreCase)) ||
               (target is not null && string.Equals(appName, System.IO.Path.GetFileNameWithoutExtension(target), StringComparison.OrdinalIgnoreCase)) ||
               string.Equals(appName, System.IO.Path.GetFileNameWithoutExtension(item.Path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Icon size that keeps the whole dock on the screen (icons shrink on a narrow monitor or with many apps).</summary>
    double EffectiveIconSize(int tiles)
    {
        var area = MonitorService.Resolve(_s.DockMonitor).WorkingArea;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double availableDip = area.Width / (scale > 0 ? scale : 1) * 0.94;
        return DockLayout.FitIconSize(_s.DockIconSize, tiles, _s.DockSpacing, availableDip);
    }

    void Rebuild()
    {
        Items.Children.Clear();
        _tiles.Clear();
        _pinnedButtons.Clear();
        _genie.Prune();
        var extra = _apps.Where(a => !_s.DockItems.Any(i => Matches(i, a))).ToList();
        int tiles = (_s.HideTaskbar ? 1 : 0) + _s.DockItems.Count + extra.Count;
        double icon = EffectiveIconSize(tiles), gap = _s.DockSpacing;
        _icon = icon;
        _sigma = DockMagnify.Sigma(icon, gap);

        // Start button: the dock stands in for the taskbar, so it must offer Start.
        if (_s.HideTaskbar)
        {
            var glyph = new Grid { Width = icon, Height = icon };
            double sq = icon * 0.34, space = icon * 0.06, pad = (icon - 2 * sq - space) / 2;
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                {
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = sq, Height = sq, RadiusX = 2, RadiusY = 2,
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(pad + c * (sq + space), pad + r * (sq + space), 0, 0)
                    };
                    rect.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "NovaAccent");
                    glyph.Children.Add(rect);
                }
            var start = BuildTile("Start", null, glyph, false, icon, gap, IntPtr.Zero, null);
            AutomationProperties.SetName(start, "Open the Start menu");
            start.Click += (_, _) => NativeMethods.PressWinKey();
            start.ContextMenuOpening += (_, e) => { e.Handled = true; NativeMethods.PressWinX(); };   // right-click on Start: Windows' own quick-link menu
            Items.Children.Add(start);
        }

        // Pinned apps. If one is already open, clicking switches to it instead of starting a second copy.
        foreach (var item in _s.DockItems.ToList())
        {
            var open = _apps.FirstOrDefault(a => Matches(item, a));
            var btn = BuildTile(item.Name, IconLoader.LoadSized(item.Path, 64), null, open is not null, icon, gap, open?.Hwnd ?? IntPtr.Zero, open?.ExePath ?? item.Path);
            AutomationProperties.SetName(btn, open is null ? $"Launch {item.Name}" : $"Switch to {item.Name}");
            AutomationProperties.SetHelpText(btn, "Alt plus Left or Right moves this shortcut. Delete removes it.");
            btn.Click += (_, _) => ClickPinned(item, btn);
            btn.ContextMenu = BuildAppMenu(btn, item.Name, open?.ExePath ?? item.Path, open, item);
            btn.KeyDown += (_, e) => OnItemKeyDown(item, e);
            _pinnedButtons[btn] = item;
            EnableDragReorder(btn, item);
            Items.Children.Add(btn);
        }

        // Open apps that are not pinned, after a thin separator (like the macOS Dock).
        if (extra.Count > 0)
        {
            var sep = new Border { Width = 1, Height = icon * 0.7, Margin = new Thickness(gap / 2, 0, gap / 2, 6), VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.4 };
            sep.SetResourceReference(Border.BackgroundProperty, "NovaBorder");
            Items.Children.Add(sep);
        }
        foreach (var app in extra)
        {
            var title = string.IsNullOrWhiteSpace(app.Title) ? "App" : app.Title;
            var label = app.ExePath is { } p ? System.IO.Path.GetFileNameWithoutExtension(p) : title;
            var btn = BuildTile(label, RunningIcon(app), null, true, icon, gap, app.Hwnd, app.ExePath);
            btn.ToolTip = title;
            AutomationProperties.SetName(btn, $"Switch to {title}");
            btn.Click += (_, _) => ClickRunning(app.Hwnd, btn, app.ExePath, null);
            btn.ContextMenu = BuildAppMenu(btn, label, app.ExePath, app, null);
            Items.Children.Add(btn);
        }
        MeasureRest();
        PopInNewTiles();
    }

    /// <summary>An app that has just opened grows into the dock (scale + fade) instead of appearing at once. Not on the first build, and not with reduced motion.</summary>
    void PopInNewTiles()
    {
        var current = new HashSet<string>();
        foreach (var t in _tiles)
        {
            var key = t.Exe is { Length: > 0 } ? "x:" + System.IO.Path.GetFileNameWithoutExtension(t.Exe).ToLowerInvariant() : "h:" + t.Hwnd.ToInt64();
            current.Add(key);
            if (_firstBuild || MotionOff || _knownTiles.Contains(key)) continue;
            var b = t.Button;
            var grow = new ScaleTransform(0.2, 0.2);
            b.RenderTransformOrigin = new Point(0.5, 1);
            b.RenderTransform = grow;
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
            var dur = TimeSpan.FromMilliseconds(320);
            var sx = new DoubleAnimation(0.2, 1, dur) { EasingFunction = ease };
            sx.Completed += (_, _) => b.RenderTransform = Transform.Identity;
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.2, 1, dur) { EasingFunction = ease });
            b.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }
        _knownTiles.Clear();
        foreach (var k in current) _knownTiles.Add(k);
        _firstBuild = false;
    }

    /// <summary>Measures the dock at rest (no zoom): its height, its width, and where each icon's centre is. The zoom is computed against these.</summary>
    void MeasureRest()
    {
        Items.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double x = 0;
        int ti = 0;
        foreach (UIElement child in Items.Children)
        {
            double w = child.DesiredSize.Width;
            if (child is Button && ti < _tiles.Count && ReferenceEquals(_tiles[ti].Button, child)) _tiles[ti++].BaseCenter = x + w / 2;
            x += w;
        }
        _baseItemsWidth = x;
        // Worked out from the icons, not from the panel, so it is right even while the panel is hidden (reveal mode).
        _restPanelHeight = Math.Max(1, Items.DesiredSize.Height + Panel.Padding.Top + Panel.Padding.Bottom + Panel.BorderThickness.Top + Panel.BorderThickness.Bottom);
    }

    /// <summary>One dock tile: just the icon (the name is in the tooltip), with a dot underneath when the app is open. Its content can be zoomed.</summary>
    Button BuildTile(string label, ImageSource? image, UIElement? glyph, bool running, double icon, double gap, IntPtr hwnd, string? exe)
    {
        var stack = new StackPanel();
        UIElement face = glyph ?? new System.Windows.Controls.Image { Width = icon, Height = icon, Source = image };
        RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.HighQuality);
        stack.Children.Add(face);
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0), Opacity = running ? 1 : 0
        };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "NovaOnSurface");
        stack.Children.Add(dot);

        var tile = new Tile { Hwnd = hwnd, Exe = exe };
        stack.LayoutTransform = tile.Zoom;   // a layout transform, so the neighbours make room as an icon grows
        tile.Button = new Button
        {
            Content = stack,
            Style = (Style)FindResource("NovaDockTile"),
            Margin = new Thickness(gap / 2, 0, gap / 2, 0),
            VerticalAlignment = VerticalAlignment.Bottom,   // every icon sits on the same baseline while one of them is bigger
            ToolTip = label
        };
        _tiles.Add(tile);
        return tile.Button;
    }

    static ImageSource? RunningIcon(RunningApp app)
    {
        if (!app.IsHostedApp && app.ExePath is not null) return IconLoader.LoadSized(app.ExePath, 64);
        try
        {
            var h = NativeMethods.WindowIcon(app.Hwnd);
            if (h == IntPtr.Zero) return null;
            var bs = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());   // the window owns the handle
            bs.Freeze();
            return bs;
        }
        catch { return null; }
    }

    void Pin(RunningApp app, string label)
    {
        if (app.ExePath is null) return;
        if (_s.DockItems.Count >= SettingsValidator.MaxDockItems) { _notify("The dock is full.", true); return; }
        if (!LaunchValidator.TryValidate(app.ExePath, out var err)) { _notify($"Can't pin that app: {err}", true); return; }
        _s.DockItems.Add(new DockItem { Name = label, Path = app.ExePath });
        _persist(_s);
        Rebuild();
    }

    // MenuItem headers treat "_" as an access key; double it so names such as "my_app" show as they are.
    static string Plain(string text) => text.Replace("_", "__");

    /// <summary>
    /// Right-click on an app in the dock, laid out like the Windows taskbar: for File Explorer the folders you open most,
    /// then the app itself (opens a new window), Pin to dock / Unpin from dock, Exit program.
    /// Pinned apps are reordered by dragging them (see the drag code below), not from the menu.
    /// </summary>
    /// <param name="open">The running copy of the app, or null when it is only pinned and not open.</param>
    /// <param name="pinned">The dock shortcut, or null when the app is open but not pinned.</param>
    ContextMenu BuildAppMenu(Button owner, string label, string? exe, RunningApp? open, DockItem? pinned)
    {
        var menu = new ContextMenu();

        // File Explorer: the folders you open most, listed straight in the menu like the Windows taskbar's jump list.
        // They are read just before the menu opens (not on every redraw of the dock), so they are always current.
        if (IsExplorer(exe, label))
        {
            var inserted = new List<UIElement>();
            owner.ContextMenuOpening += (_, _) => FillFrequentFolders(menu, inserted);
        }

        // The app itself: a new window (or start it, when it is not open).
        if (exe is not null)
        {
            var launchItem = pinned ?? new DockItem { Name = label, Path = exe };
            var launch = new MenuItem { Header = Plain(label), FontWeight = FontWeights.SemiBold };
            launch.Click += (_, _) => Launch(launchItem);
            menu.Items.Add(launch);

            if (pinned is null)
            {
                var pin = new MenuItem { Header = "Pin to dock" };
                pin.Click += (_, _) => { if (open is not null) Pin(open, label); };
                menu.Items.Add(pin);
            }
        }
        if (pinned is not null)
        {
            var unpin = new MenuItem { Header = "Unpin from dock" };
            unpin.Click += (_, _) => Remove(pinned);
            menu.Items.Add(unpin);
        }

        if (open is not null)
        {
            if (!open.IsHostedApp)
            {
                var insertedWindows = new List<UIElement>();
                owner.ContextMenuOpening += (_, _) => FillWindows(menu, insertedWindows, open.Hwnd);
            }
            menu.Items.Add(new Separator());
            var exit = new MenuItem { Header = "Exit program" };
            exit.Click += (_, _) => CloseWithAnimation(owner, open.Hwnd, () => AppControl.ExitProgram(open.Hwnd, open.IsHostedApp, ConfirmEnd));
            menu.Items.Add(exit);
        }

        menu.Opened += (_, _) => _modal = true;
        menu.Closed += (_, _) => { _modal = false; if (RevealMode) _hideTimer.Start(); };
        AutoCloseWhenPointerLeaves(menu, owner);
        return menu;
    }

    /// <summary>
    /// Closing an app: its window shrinks into its dock icon (the same flight as minimizing), then the real close or exit follows.
    /// Without animation, or if the window is already minimized or busy, it closes at once.
    /// </summary>
    void CloseWithAnimation(Button tile, IntPtr hwnd, Action close)
    {
        CloseWithAnimationCore(tile, hwnd, close);
        RefreshSoon();   // the tile of a closed app should go away without waiting for the slow poll
    }

    void CloseWithAnimationCore(Button tile, IntPtr hwnd, Action close)
    {
        if (MotionOff || _genie.Busy || !NativeMethods.IsWindow(hwnd) || RunningAppsService.IsMinimized(hwnd)) { close(); return; }
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0) dpi = 1;
        var centre = tile.PointToScreen(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2));
        double iconPx = Math.Max(16, _icon * dpi);
        _genie.Minimize(hwnd, centre, iconPx, true);
        var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        wait.Tick += (_, _) => { wait.Stop(); try { close(); } catch (Exception ex) { FileLog.Write($"Dock close failed: {ex.GetType().Name}"); } };
        wait.Start();
    }

    static bool IsExplorer(string? exe, string label)
    {
        var name = exe is null ? "" : System.IO.Path.GetFileNameWithoutExtension(exe);
        return name.Equals("explorer", StringComparison.OrdinalIgnoreCase) || name.Equals("File Explorer", StringComparison.OrdinalIgnoreCase) ||
               label.Equals("explorer", StringComparison.OrdinalIgnoreCase) || label.Equals("File Explorer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>When a program has more than one window, lists them at the top of its menu so each can be picked directly.</summary>
    void FillWindows(ContextMenu menu, List<UIElement> inserted, IntPtr hwnd)
    {
        foreach (var old in inserted) menu.Items.Remove(old);
        inserted.Clear();
        if (!NativeMethods.IsWindow(hwnd)) return;
        var windows = AppControl.WindowsOfProgram(hwnd);
        if (windows.Count < 2) return;
        int at = 0;
        void Put(UIElement item) { menu.Items.Insert(at++, item); inserted.Add(item); }
        foreach (var w in windows)
        {
            var title = NativeMethods.WindowText(w);
            if (title.Length > 60) title = title[..60] + "…";
            var entry = new MenuItem { Header = Plain(title) };
            var target = w;
            entry.Click += (_, _) => RunningAppsService.Activate(target);
            Put(entry);
        }
        Put(new Separator());
    }

    /// <summary>Puts a "Frequent" heading and the frequently used folders at the top of Explorer's menu (replacing what an earlier opening put there).</summary>
    void FillFrequentFolders(ContextMenu menu, List<UIElement> inserted)
    {
        foreach (var old in inserted) menu.Items.Remove(old);
        inserted.Clear();
        int at = 0;
        void Put(UIElement item) { menu.Items.Insert(at++, item); inserted.Add(item); }

        Put(new MenuItem { Header = "Quick access", IsEnabled = false, FontWeight = FontWeights.SemiBold });
        var folders = AppControl.QuickAccessFolders();
        if (folders.Count == 0) Put(new MenuItem { Header = "No folders yet", IsEnabled = false });
        foreach (var (name, path) in folders)
        {
            var folder = new MenuItem { Header = Plain(name), ToolTip = path };
            folder.Click += (_, _) => AppControl.OpenFolder(path);
            Put(folder);
        }
        Put(new Separator());
    }

    /// <summary>Asked only when a program did not close after "Exit program". Ending it loses unsaved work, so the default answer is No.</summary>
    bool ConfirmEnd(string programName) =>
        MessageBox.Show($"{programName} did not close. End it now? Unsaved work in it will be lost.", "NOVA",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    void OnItemKeyDown(DockItem item, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Left) { Move(item, -1); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Right) { Move(item, +1); e.Handled = true; }
        else if (key == Key.Delete) { Remove(item); e.Handled = true; }
    }

    void Move(DockItem item, int delta)
    {
        var from = _s.DockItems.IndexOf(item);
        var to = ListReorder.Move(_s.DockItems, from, delta);
        if (to == from) return;
        var hadFocus = IsKeyboardFocusWithin;
        _persist(_s);
        Rebuild();
        FocusIndexLater(to, hadFocus);
    }

    void Remove(DockItem item)
    {
        var idx = _s.DockItems.IndexOf(item);
        var hadFocus = IsKeyboardFocusWithin;
        _s.DockItems.Remove(item);
        _persist(_s);
        Rebuild();
        FocusIndexLater(Math.Min(idx, _s.DockItems.Count), hadFocus);   // neighbour
    }

    // Rebuilding removes the focused button, so remember whether keyboard focus was here and restore it afterwards.
    void FocusIndexLater(int index, bool hadFocus) => Dispatcher.InvokeAsync(() =>
    {
        // The Start button (when shown) occupies the first slot, so pinned item i sits at i + 1.
        int slot = index + (_s.HideTaskbar ? 1 : 0);
        var buttons = Items.Children.OfType<Button>().ToList();
        if (hadFocus && buttons.Count > 0) buttons[Math.Min(Math.Max(slot, 0), buttons.Count - 1)].Focus();
    }, DispatcherPriority.Input);

    readonly Dictionary<string, DateTime> _launchedAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A click on a pinned app. What to do is decided now, from a fresh look at the open apps, and not from what the dock knew when it was
    /// last drawn (which can be several seconds old, or frozen while the pointer is on the dock). Open: show it, or put it away if it is
    /// already in front. Not open: start it, once; a second click while it is still starting does nothing instead of opening another copy.
    /// </summary>
    void ClickPinned(DockItem item, Button btn)
    {
        var open = _running.Snapshot().FirstOrDefault(a => Matches(item, a));
        if (open is not null)
        {
            _launchedAt.Remove(item.Path);
            ClickRunning(open.Hwnd, btn, open.ExePath ?? item.Path, item);
            return;
        }
        if (_launchedAt.TryGetValue(item.Path, out var when) && DateTime.UtcNow - when < TimeSpan.FromSeconds(6)) return;   // still starting
        Bounce(btn);
        Launch(item);
    }

    void Launch(DockItem item)
    {
        if (!LaunchValidator.TryValidate(item.Path, out var err))
        {
            FileLog.Write($"Dock launch blocked for '{item.Name}': {err}");
            _notify($"Can't open {item.Name}: {err}", true);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
            _launchedAt[item.Path] = DateTime.UtcNow;
            RefreshSoon();   // show the new app's icon as soon as its window exists
        }
        catch (Exception ex)
        {
            FileLog.Write($"Dock launch failed for '{item.Name}': {ex.GetType().Name}");
            _notify($"Couldn't start {item.Name}.", true);
        }
    }

    void Add()
    {
        if (_s.DockItems.Count >= SettingsValidator.MaxDockItems) { _notify("The dock is full.", true); return; }
        var dlg = new OpenFileDialog { Filter = "Applications and shortcuts|*.exe;*.lnk", Title = "Add to NOVA dock" };
        _modal = true;
        bool ok;
        try { ok = dlg.ShowDialog(this) == true; } finally { _modal = false; }
        if (RevealMode) _hideTimer.Start();
        if (!ok) return;
        AddPath(dlg.FileName);
    }

    void AddPath(string path)
    {
        if (_s.DockItems.Count >= SettingsValidator.MaxDockItems) { _notify("The dock is full.", true); return; }
        if (!LaunchValidator.TryValidate(path, out var err)) { _notify($"Can't add that file: {err}", true); return; }
        _s.DockItems.Add(new DockItem { Name = System.IO.Path.GetFileNameWithoutExtension(path), Path = path });
        _persist(_s);
        Rebuild();
    }

    // Drag a pinned app left or right to move it (this replaces the old Move left / Move right menu items).
    void EnableDragReorder(Button btn, DockItem item)
    {
        Point? down = null;
        btn.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(this);
        btn.PreviewMouseLeftButtonUp += (_, _) => down = null;
        btn.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || down is not { } d) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            down = null;
            _dragItem = item;
            try { DragDrop.DoDragDrop(btn, new DataObject(DataFormats.Text, item.Name), DragDropEffects.Move); }
            catch (Exception ex) { FileLog.Write($"Dock drag failed: {ex.GetType().Name}"); }
            finally { _dragItem = null; }
        };
    }

    /// <summary>Puts the dragged app where it was dropped: after every other pinned icon whose centre lies to the left of the pointer.</summary>
    void DropReorder(DockItem item, DragEventArgs e)
    {
        int from = _s.DockItems.IndexOf(item);
        if (from < 0) return;
        double x = e.GetPosition(Items).X;
        int to = 0;
        foreach (var other in _s.DockItems)
        {
            if (ReferenceEquals(other, item)) continue;
            var b = _pinnedButtons.FirstOrDefault(kv => ReferenceEquals(kv.Value, other)).Key;
            if (b is null) continue;
            if (b.TranslatePoint(new Point(b.ActualWidth / 2, 0), Items).X < x) to++;
        }
        if (to == from) return;
        _s.DockItems.RemoveAt(from);
        _s.DockItems.Insert(Math.Min(to, _s.DockItems.Count), item);
        _persist(_s);
        Rebuild();
    }

    // Drag an app or shortcut onto the dock to pin it.
    void OnPanelDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _dragItem is not null ? DragDropEffects.Move : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    void OnPanelDrop(object sender, DragEventArgs e)
    {
        if (_dragItem is { } moved) { DropReorder(moved, e); e.Handled = true; return; }
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var f in files)
        {
            var ext = System.IO.Path.GetExtension(f);
            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) AddPath(f);
        }
        e.Handled = true;
    }

    bool _placing;

    /// <summary>Re-centres on the configured monitor and re-fits the icons. Also called after monitors or scaling change.</summary>
    public void Relocate() { _reserve.Relocate(); Rebuild(); Reposition(); }

    /// <summary>Wide mode: the window is as wide as the monitor and tall enough for the resting dock plus the room the zoom needs.</summary>
    void FitWindow()
    {
        if (!Wide) { if (SizeToContent != SizeToContent.WidthAndHeight) SizeToContent = SizeToContent.WidthAndHeight; return; }
        var scr = MonitorService.Resolve(_s.DockMonitor);
        var area = _s.HideTaskbar ? scr.Bounds : scr.WorkingArea;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0) dpi = 1;
        double w = Math.Floor(area.Width / dpi);
        double h = Math.Ceiling(Headroom + _restPanelHeight + _s.DockBottomOffset);
        if (SizeToContent != SizeToContent.Manual) SizeToContent = SizeToContent.Manual;
        if (Math.Abs(Width - w) > 0.5 || double.IsNaN(Width)) Width = w;
        if (Math.Abs(Height - h) > 0.5 || double.IsNaN(Height)) Height = h;
    }

    void Reposition()
    {
        if (_placing) return;
        _placing = true;
        try
        {
            FitWindow();
            double hDip = Wide ? Height : ActualHeight;
            if (hDip <= 0 || double.IsNaN(hDip)) { Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); hDip = DesiredSize.Height; }

            if (ReserveSpace && !_fullscreen)
            {
                // Tell Windows how much room the resting dock takes, then sit exactly inside that strip.
                // The see-through head-room above it is not reserved: it may overlap windows (clicks fall through it).
                _reserve.Apply(_s.DockMonitor, Math.Max(1, _restPanelHeight + _s.DockBottomOffset));
                if (_reserve.PlacedBottomPx is int bottom) { WindowPlacement.PlaceCenteredAtBottom(this, _s.DockMonitor, bottom); return; }   // the strip's bottom row = the dock's bottom row
            }
            else _reserve.Release();

            WindowPlacement.PlaceCentered(this, _s.DockMonitor, _s.HideTaskbar ? PlacementEdge.BottomFlush : PlacementEdge.Bottom, 0);
        }
        finally { _placing = false; }
    }

    // ---- clicking an open app: open it, click again to put it away ----

    /// <summary>The app's current window, found afresh (used when the one the tile remembers is gone).</summary>
    IntPtr FindWindowOf(string? exe)
    {
        if (string.IsNullOrEmpty(exe)) return IntPtr.Zero;
        var name = System.IO.Path.GetFileNameWithoutExtension(exe);
        var hit = _running.Snapshot().FirstOrDefault(a => a.ExePath is { } p &&
            (string.Equals(p, exe, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(System.IO.Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase)));
        return hit?.Hwnd ?? IntPtr.Zero;
    }

    /// <param name="exe">The program behind the tile, to find its window again if the remembered one has closed.</param>
    /// <param name="pinned">The dock shortcut: if the program is not open any more, clicking starts it instead of doing nothing.</param>
    void ClickRunning(IntPtr hwnd, Button tile, string? exe = null, DockItem? pinned = null)
    {
        if (!NativeMethods.IsWindow(hwnd))
        {
            // The dock is redrawn every 2 s, so a click can arrive for a window that has just closed or been replaced.
            hwnd = FindWindowOf(exe);
            if (hwnd == IntPtr.Zero)
            {
                if (pinned is not null && !(_launchedAt.TryGetValue(pinned.Path, out var when) && DateTime.UtcNow - when < TimeSpan.FromSeconds(6))) { Bounce(tile); Launch(pinned); }
                Dispatcher.InvokeAsync(() => RefreshRunning(force: true), DispatcherPriority.Background);
                return;
            }
        }
        if (_genie.Busy) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0) dpi = 1;
        var centre = tile.PointToScreen(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2));   // physical pixels
        double iconPx = Math.Max(16, _icon * dpi * (_tiles.FirstOrDefault(t => ReferenceEquals(t.Button, tile))?.Scale ?? 1));
        bool animate = !MotionOff;

        // A program with several windows (e.g. two browser windows) is one dock icon. Clicking it while one of its windows is in front
        // brings the next window forward instead of putting the program away, so the second window is never unreachable.
        var siblings = NativeMethods.IsWindow(hwnd) ? AppControl.WindowsOfProgram(hwnd) : Array.Empty<IntPtr>();
        if (siblings.Count > 1)
        {
            var front = NativeMethods.ForegroundRoot();
            if (siblings.Contains(front))
            {
                RunningAppsService.Activate(siblings[siblings.Count - 1]);   // the window that has been in the back the longest
                return;
            }
        }

        if (RunningAppsService.IsMinimized(hwnd)) _genie.Restore(hwnd, centre, iconPx, animate);
        else if (RunningAppsService.IsForeground(hwnd)) _genie.Minimize(hwnd, centre, iconPx, animate);
        else RunningAppsService.Activate(hwnd);
    }

    // ---- a little hop, like the Mac dock when an app opens or wants attention ----

    void Bounce(Button b)
    {
        if (MotionOff) return;
        var move = new TranslateTransform();
        b.RenderTransform = move;
        var hop = new DoubleAnimation(0, -Math.Max(10, _icon * 0.55), TimeSpan.FromMilliseconds(240))
        {
            AutoReverse = true, RepeatBehavior = new RepeatBehavior(3),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        hop.Completed += (_, _) => b.RenderTransform = Transform.Identity;
        move.BeginAnimation(TranslateTransform.YProperty, hop);
    }

    /// <summary>A window asked for attention (its taskbar button would flash): hop its dock icon.</summary>
    public void Bounce(IntPtr hwnd, string? exePath)
    {
        var tile = _tiles.FirstOrDefault(t => t.Hwnd != IntPtr.Zero && t.Hwnd == hwnd)
                   ?? _tiles.FirstOrDefault(t => exePath is not null && t.Exe is not null && string.Equals(t.Exe, exePath, StringComparison.OrdinalIgnoreCase));
        if (tile is not null) Bounce(tile.Button);
    }

    // ---- zoom under the pointer ----

    void OnPanelMouseMove(object sender, MouseEventArgs e) => StartZoomLoop();

    void StartZoomLoop()
    {
        if (!MagnifyOn || _magHooked) return;
        CompositionTarget.Rendering += OnZoomFrame;
        _magHooked = true;
    }

    void StopZoomLoop()
    {
        if (!_magHooked) return;
        CompositionTarget.Rendering -= OnZoomFrame;
        _magHooked = false;
    }

    void ResetZoom()
    {
        foreach (var t in _tiles) { t.Scale = 1; t.Zoom.ScaleX = 1; t.Zoom.ScaleY = 1; }
        StopZoomLoop();
    }

    void OnZoomFrame(object? sender, EventArgs e)
    {
        if (!MagnifyOn) { ResetZoom(); return; }
        bool inside = Panel.IsMouseOver && Panel.Visibility == Visibility.Visible;
        double pointer = double.NaN;
        if (inside)
        {
            double cur = Items.ActualWidth, x = Mouse.GetPosition(Items).X;
            // The zoom is worked out against the resting layout, so growing icons don't push the pointer's target around.
            pointer = cur > 1 && _baseItemsWidth > 1 ? (x - cur / 2) * (_baseItemsWidth / cur) + _baseItemsWidth / 2 : x;
        }

        bool resting = true;
        foreach (var t in _tiles)
        {
            double target = inside ? DockMagnify.Scale(pointer, t.BaseCenter, _sigma, _s.DockMagnifyScale) : 1;
            double next = DockMagnify.Approach(t.Scale, target);
            if (next != t.Scale) { t.Scale = next; t.Zoom.ScaleX = next; t.Zoom.ScaleY = next; }
            if (next != 1) resting = false;
        }
        if (!inside && resting) StopZoomLoop();
    }

    void OnSizeChanged(object sender, SizeChangedEventArgs e) => Reposition();
    void OnSourceInitialized(object? sender, EventArgs e) => NativeMethods.SetOverlayStyle(this, noActivate: true);
    void OnMouseEnter(object sender, MouseEventArgs e) { _hideTimer.Stop(); if (RevealMode) SetRevealed(true); }
    void OnMouseLeave(object sender, MouseEventArgs e) { if (RevealMode) _hideTimer.Start(); }
    void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { NativeMethods.SetOverlayStyle(this, noActivate: true); if (RevealMode) { _hideTimer.Stop(); _modal = false; SetRevealed(false); } }
    }

    void OnKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
        {
            NativeMethods.SetOverlayStyle(this, noActivate: true);   // never keep taking focus after the user leaves
            if (RevealMode) _hideTimer.Start();
        }
    }
}
