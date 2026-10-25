using System.Windows;
using System.Windows.Media;
using Nova.App.Integrations.Windows;

namespace Nova.App.Features.FloatingDock;

/// <summary>
/// An invisible, click-through strip registered as a bottom (dock) or top (island-only mode) AppBar. It does one job: it tells Windows "this much of the
/// screen is taken", exactly like the taskbar does, so a maximized browser stops above the dock instead of going under it.
/// </summary>
internal sealed class DockReserveWindow : Window
{
    readonly AppBarService _bar;
    string? _monitor;
    double _heightDip;

    public event Action<bool>? FullscreenChanged;
    public event Action? Repositioned;

    /// <summary>Bottom screen row (physical pixels) of the reserved strip, or null while nothing is reserved.</summary>
    public int? PlacedBottomPx => _bar.IsRegistered && _bar.PlacedBottom != int.MinValue ? _bar.PlacedBottom : null;

    public DockReserveWindow(bool bottom = true)
    {
        Title = bottom ? "NOVA dock space" : "NOVA top space";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Width = 1; Height = 1;
        SourceInitialized += (_, _) => NativeMethods.SetClickThrough(this);

        _bar = new AppBarService(this, bottom);
        _bar.FullscreenChanged += f => FullscreenChanged?.Invoke(f);
        _bar.Repositioned += () => Repositioned?.Invoke();
        Closed += (_, _) => _bar.Dispose();
    }

    /// <summary>Reserves a strip of the given height at the bottom (or top) of the monitor. Cheap to call repeatedly.</summary>
    public bool Apply(string? monitor, double heightDip)
    {
        if (!IsVisible) Show();
        if (_bar.IsRegistered && string.Equals(_monitor ?? "", monitor ?? "", StringComparison.OrdinalIgnoreCase) && Math.Abs(_heightDip - heightDip) < 0.5)
            return true;
        _monitor = monitor;
        _heightDip = heightDip;
        return _bar.Dock(monitor, heightDip);
    }

    /// <summary>Gives the strip back to Windows right away; <see cref="Apply"/> can reserve it again later.</summary>
    public void Release()
    {
        _bar.Release();
        _heightDip = 0;
        if (IsVisible) Hide();
    }

    /// <summary>Re-reserves after a monitor, resolution or scale change.</summary>
    public void Relocate() { if (_bar.IsRegistered) _bar.Reposition(); }
}
