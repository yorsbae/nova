using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    readonly DispatcherTimer _runTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DockReserveWindow _reserve = new();
    IReadOnlyList<RunningApp> _apps = Array.Empty<RunningApp>();
    string _appsKey = "";
    NovaSettings _s;
    bool _revealed = true, _modal, _fullscreen;

    public DockWindow(NovaSettings settings, bool opaque, Action<NovaSettings> persist, Action<string, bool> notify)
    {
        _s = settings; _persist = persist; _notify = notify;
        InitializeComponent();
        OverlayChrome.Configure(this, opaque);
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); TryHide(); };
        _runTimer.Tick += (_, _) => PollRunning();
        _reserve.FullscreenChanged += SetFullscreen;
        _reserve.Repositioned += () => { if (!_placing) Reposition(); };
        Closed += (_, _) => { _hideTimer.Stop(); _runTimer.Stop(); _reserve.Close(); };

        // Right-click on the dock itself (not on an icon): add an app. There is no permanent "+" button taking up room.
        var menu = new ContextMenu();
        var add = new MenuItem { Header = "Add app…" };
        add.Click += (_, _) => Add();
        menu.Items.Add(add);
        menu.Opened += (_, _) => _modal = true;
        menu.Closed += (_, _) => { _modal = false; if (RevealMode) _hideTimer.Start(); };
        Panel.ContextMenu = menu;

        ApplySettings(settings);
    }

    bool RevealMode => _s.DockVisibility == DockVisibility.RevealOnEdgeHover;
    bool ReserveSpace => _s.DockReservesSpace && !RevealMode;

    public void ApplySettings(NovaSettings s)
    {
        _s = s;
        Panel.Margin = new Thickness(0, 0, 0, s.DockBottomOffset);
        // Transparent head-room above the panel lets a zoomed icon grow past the panel. An opaque window can't be see-through, so it gets none.
        Root.Margin = new Thickness(0, AllowsTransparency ? 8 : 0, 0, 0);
        // In reveal mode the whole window area must be hit-testable so moving the mouse within it doesn't hide the dock.
        Root.Background = RevealMode ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : null;
        if (s.ShowRunningApps)
        {
            _apps = _running.Snapshot();
            _appsKey = AppsKey(_apps);
            _runTimer.Start();
        }
        else { _runTimer.Stop(); _apps = Array.Empty<RunningApp>(); _appsKey = ""; }
        ApplyGlass();
        Rebuild();
        SetRevealed(!RevealMode);
        Reposition();
    }

    /// <summary>Re-reads theme colors (Windows light/dark or high-contrast change).</summary>
    public void RefreshTheme() => ApplyGlass();

    /// <summary>Glass look: a see-through strip, so the Start button and app icons float over the wallpaper. High contrast always uses solid system colors.</summary>
    void ApplyGlass()
    {
        bool highContrast = SystemParameters.HighContrast || _s.HighContrastOverride;
        if (_s.DockGlass && AllowsTransparency && !highContrast)
        {
            var c = (Application.Current.Resources["NovaWindow"] as SolidColorBrush)?.Color ?? Colors.Black;
            c.A = 0x59;
            var fill = new SolidColorBrush(c); fill.Freeze();
            var edge = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)); edge.Freeze();
            Panel.Background = fill;
            Panel.BorderBrush = edge;
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

    /// <summary>Checks which apps are open (every 2 s, only while the dock is enabled) and redraws only if that changed.</summary>
    void PollRunning()
    {
        if (_modal || _fullscreen) return;
        var snap = _running.Snapshot();
        var key = AppsKey(snap);
        if (key == _appsKey) return;
        if (IsKeyboardFocusWithin) return;   // never pull the buttons away from a keyboard user; try again next tick
        _apps = snap; _appsKey = key;
        Rebuild();
    }

    static bool Matches(DockItem item, RunningApp app)
    {
        if (app.ExePath is not { } exe) return false;
        return string.Equals(exe, item.Path, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(System.IO.Path.GetFileNameWithoutExtension(exe), System.IO.Path.GetFileNameWithoutExtension(item.Path), StringComparison.OrdinalIgnoreCase);
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
        var extra = _apps.Where(a => !_s.DockItems.Any(i => Matches(i, a))).ToList();
        int tiles = (_s.HideTaskbar ? 1 : 0) + _s.DockItems.Count + extra.Count;
        double icon = EffectiveIconSize(tiles), gap = _s.DockSpacing;

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
            var start = BuildTile("Start", null, glyph, false, icon, gap);
            AutomationProperties.SetName(start, "Open the Start menu");
            start.Click += (_, _) => NativeMethods.PressWinKey();
            Items.Children.Add(start);
        }

        // Pinned apps. If one is already open, clicking switches to it instead of starting a second copy.
        foreach (var item in _s.DockItems.ToList())
        {
            var open = _apps.FirstOrDefault(a => Matches(item, a));
            var btn = BuildTile(item.Name, IconLoader.Load(item.Path), null, open is not null, icon, gap);
            AutomationProperties.SetName(btn, open is null ? $"Launch {item.Name}" : $"Switch to {item.Name}");
            AutomationProperties.SetHelpText(btn, "Alt plus Left or Right moves this shortcut. Delete removes it.");
            btn.Click += (_, _) => { if (open is not null) RunningAppsService.Activate(open.Hwnd); else Launch(item); };
            btn.ContextMenu = BuildMenu(item);
            btn.KeyDown += (_, e) => OnItemKeyDown(item, e);
            Items.Children.Add(btn);
        }

        // Open apps that are not pinned, after a thin separator (like the macOS Dock).
        if (extra.Count > 0)
        {
            var sep = new Border { Width = 1, Height = icon * 0.7, Margin = new Thickness(gap / 2, 0, gap / 2, 6), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.4 };
            sep.SetResourceReference(Border.BackgroundProperty, "NovaBorder");
            Items.Children.Add(sep);
        }
        foreach (var app in extra)
        {
            var title = string.IsNullOrWhiteSpace(app.Title) ? "App" : app.Title;
            var label = app.ExePath is { } p ? System.IO.Path.GetFileNameWithoutExtension(p) : title;
            var btn = BuildTile(label, RunningIcon(app), null, true, icon, gap);
            btn.ToolTip = title;
            AutomationProperties.SetName(btn, $"Switch to {title}");
            btn.Click += (_, _) => RunningAppsService.Activate(app.Hwnd);
            if (app.ExePath is not null) btn.ContextMenu = BuildRunningMenu(app, label);
            Items.Children.Add(btn);
        }
    }

    /// <summary>One dock tile: just the icon (the name is in the tooltip), with a dot underneath when the app is open.</summary>
    Button BuildTile(string label, ImageSource? image, UIElement? glyph, bool running, double icon, double gap)
    {
        var stack = new StackPanel();
        stack.Children.Add(glyph ?? new System.Windows.Controls.Image { Width = icon, Height = icon, Source = image });
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0), Opacity = running ? 1 : 0
        };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "NovaOnSurface");
        stack.Children.Add(dot);
        return new Button
        {
            Content = stack,
            Style = (Style)FindResource("NovaDockTile"),
            Margin = new Thickness(gap / 2, 0, gap / 2, 0),
            ToolTip = label
        };
    }

    static ImageSource? RunningIcon(RunningApp app)
    {
        if (!app.IsHostedApp && app.ExePath is not null) return IconLoader.Load(app.ExePath);
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

    ContextMenu BuildRunningMenu(RunningApp app, string label)
    {
        var menu = new ContextMenu();
        var keep = new MenuItem { Header = $"Keep {label} in dock" };
        keep.Click += (_, _) => Pin(app, label);
        menu.Items.Add(keep);
        menu.Opened += (_, _) => _modal = true;
        menu.Closed += (_, _) => { _modal = false; if (RevealMode) _hideTimer.Start(); };
        return menu;
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

    ContextMenu BuildMenu(DockItem item)
    {
        var menu = new ContextMenu();
        var left = new MenuItem { Header = "Move left" };   left.Click += (_, _) => Move(item, -1);
        var right = new MenuItem { Header = "Move right" }; right.Click += (_, _) => Move(item, +1);
        var remove = new MenuItem { Header = $"Remove {item.Name}" }; remove.Click += (_, _) => Remove(item);
        menu.Items.Add(left); menu.Items.Add(right); menu.Items.Add(new Separator()); menu.Items.Add(remove);
        menu.Opened += (_, _) => _modal = true;
        menu.Closed += (_, _) => { _modal = false; if (RevealMode) _hideTimer.Start(); };
        return menu;
    }

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

    void Launch(DockItem item)
    {
        if (!LaunchValidator.TryValidate(item.Path, out var err))
        {
            FileLog.Write($"Dock launch blocked for '{item.Name}': {err}");
            _notify($"Can't open {item.Name}: {err}", true);
            return;
        }
        try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); }
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

    // Drag an app or shortcut onto the dock to pin it.
    void OnPanelDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    void OnPanelDrop(object sender, DragEventArgs e)
    {
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

    void Reposition()
    {
        if (_placing) return;
        _placing = true;
        try
        {
            double hDip = ActualHeight;
            if (hDip <= 0) { Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); hDip = DesiredSize.Height; }

            if (ReserveSpace && !_fullscreen)
            {
                // Tell Windows how much room the dock takes, then sit exactly inside that strip.
                // The transparent head-room is not reserved: it may overlap windows (it is click-through).
                _reserve.Apply(_s.DockMonitor, Math.Max(1, hDip - Root.Margin.Top));
                if (_reserve.PlacedBottomPx is int bottom) { WindowPlacement.PlaceCenteredAtBottom(this, _s.DockMonitor, bottom); return; }   // the strip's bottom row = the dock's bottom row
            }
            else _reserve.Release();

            WindowPlacement.PlaceCentered(this, _s.DockMonitor, _s.HideTaskbar ? PlacementEdge.BottomFlush : PlacementEdge.Bottom, 0);
        }
        finally { _placing = false; }
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
