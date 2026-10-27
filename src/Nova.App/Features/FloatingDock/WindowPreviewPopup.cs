using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nova.App.Integrations.Windows;

namespace Nova.App.Features.FloatingDock;

/// <summary>
/// The small window previews that pop up above a dock icon (like the Windows taskbar): one card per window of the program, with its title,
/// a picture of the window and a close button. A click on a card brings that window forward. It is always kept inside the monitor, so
/// the second, third, ... card is never pushed off the edge of the screen. It never takes focus.
/// </summary>
internal sealed class WindowPreviewPopup : Window
{
    const double CardW = 220, ThumbH = 124, EdgeMargin = 8;
    const int MaxCards = 12;

    readonly WrapPanel _row = new() { Orientation = Orientation.Horizontal };
    readonly Border _frame;
    readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly Action<IntPtr> _activate, _close;
    int _generation;

    public WindowPreviewPopup(bool opaque, Action<IntPtr> activate, Action<IntPtr> close)
    {
        _activate = activate; _close = close;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -20000; Top = -20000;
        OverlayChrome.Configure(this, opaque);
        SourceInitialized += (_, _) => NativeMethods.SetOverlayStyle(this, noActivate: true);

        _frame = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(6), BorderThickness = new Thickness(1), Child = _row };
        _frame.SetResourceReference(Border.BackgroundProperty, "NovaSurface");
        _frame.SetResourceReference(Border.BorderBrushProperty, "NovaBorder");
        Content = _frame;

        _hide.Tick += (_, _) => { _hide.Stop(); if (!IsMouseOver) HideNow(); };
        MouseEnter += (_, _) => _hide.Stop();
        MouseLeave += (_, _) => HideSoon();
    }

    public void CancelHide() => _hide.Stop();

    public void HideSoon() { _hide.Stop(); if (IsVisible) _hide.Start(); }

    public void HideNow()
    {
        _hide.Stop();
        _generation++;   // pictures still being taken are dropped
        _row.Children.Clear();
        if (IsVisible) Hide();
    }

    /// <param name="monitorPx">The monitor the dock is on, in physical pixels; the popup never leaves it.</param>
    public void ShowFor(Button tile, IReadOnlyList<IntPtr> windows, System.Drawing.Rectangle monitorPx, double dpi)
    {
        _hide.Stop();
        int gen = ++_generation;
        _row.Children.Clear();
        _row.MaxWidth = Math.Max(CardW + 24, monitorPx.Width / dpi - 2 * (EdgeMargin + 8));
        foreach (var h in windows.Take(MaxCards)) _row.Children.Add(BuildCard(h, gen));
        if (_row.Children.Count == 0) { HideNow(); return; }

        _frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double wPx = _frame.DesiredSize.Width * dpi, hPx = _frame.DesiredSize.Height * dpi;
        var tl = tile.PointToScreen(new Point(0, 0));   // physical pixels
        double tileWPx = tile.ActualWidth * dpi, tileHPx = tile.ActualHeight * dpi, gap = 10 * dpi, edge = EdgeMargin * dpi;

        double x = tl.X + tileWPx / 2 - wPx / 2;
        x = Math.Max(monitorPx.Left + edge, Math.Min(x, monitorPx.Right - wPx - edge));   // keep every card on the screen
        double y = tl.Y - hPx - gap;
        if (y < monitorPx.Top + edge) y = tl.Y + tileHPx + gap;   // no room above (dock at the top): go below
        y = Math.Max(monitorPx.Top + edge, Math.Min(y, monitorPx.Bottom - hPx - edge));

        if (!IsVisible) Show();
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(handle, new IntPtr(-1) /* HWND_TOPMOST */, (int)Math.Round(x), (int)Math.Round(y), 0, 0, 0x0001 /* NOSIZE */ | 0x0010 /* NOACTIVATE */);
    }

    UIElement BuildCard(IntPtr h, int gen)
    {
        var title = NativeMethods.WindowText(h);
        if (title.Length > 80) title = title[..80] + "…";
        var iconSource = WindowIconSource(h);

        var titleText = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        var icon = new Image { Width = 16, Height = 16, Source = iconSource, Margin = new Thickness(0, 0, 6, 0) };
        var close = new Button
        {
            Content = "✕", Width = 22, Height = 22, Padding = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "Close window", Focusable = false
        };
        close.SetResourceReference(Control.ForegroundProperty, "NovaOnSurface");
        var header = new DockPanel { Width = CardW, LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(icon);
        header.Children.Add(close);
        header.Children.Add(titleText);

        var thumb = new Image { Width = CardW, Height = ThumbH, Stretch = Stretch.None, Source = iconSource, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.HighQuality);

        var card = new Border
        {
            Child = new StackPanel { Children = { header, thumb } },
            Padding = new Thickness(8), Margin = new Thickness(4), CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent, Cursor = Cursors.Hand
        };
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "NovaSurfaceAlt");
        card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
        card.MouseLeftButtonUp += (_, e) => { if (e.Handled) return; e.Handled = true; _activate(h); HideNow(); };
        close.Click += (_, e) =>
        {
            e.Handled = true;
            _close(h);
            _row.Children.Remove(card);
            if (_row.Children.Count == 0) HideNow();
        };

        _ = FillThumbnailAsync(thumb, h, gen);
        return card;
    }

    async Task FillThumbnailAsync(Image target, IntPtr h, int gen)
    {
        BitmapSource? picture = null;
        try { picture = await Task.Run(() => Capture(h)); }
        catch { /* the window closed meanwhile: keep the icon */ }
        if (picture is null || gen != _generation) return;
        target.Stretch = Stretch.Uniform;
        target.Source = picture;
    }

    // ---- taking the picture ----

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>A picture of the window's content (also when it is covered by other windows). Null for a minimized or unreadable window.</summary>
    static BitmapSource? Capture(IntPtr h)
    {
        if (!NativeMethods.IsWindow(h) || NativeMethods.IsIconic(h) || !NativeMethods.GetWindowRect(h, out var r)) return null;
        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        if (w < 16 || ht < 16 || w > 8192 || ht > 8192) return null;

        IntPtr screen = GetDC(IntPtr.Zero), mem = IntPtr.Zero, bmp = IntPtr.Zero;
        try
        {
            mem = CreateCompatibleDC(screen);
            bmp = CreateCompatibleBitmap(screen, w, ht);
            if (mem == IntPtr.Zero || bmp == IntPtr.Zero) return null;
            var old = SelectObject(mem, bmp);
            bool ok = NativeMethods.PrintWindow(h, mem, 0x2 /* PW_RENDERFULLCONTENT */);
            SelectObject(mem, old);
            if (!ok) return null;
            var raw = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var opaque = new FormatConvertedBitmap(raw, PixelFormats.Bgr32, null, 0);   // no transparency, whatever the app left in the alpha bytes
            opaque.Freeze();
            return opaque;
        }
        catch { return null; }
        finally
        {
            if (bmp != IntPtr.Zero) NativeMethods.DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    static ImageSource? WindowIconSource(IntPtr h)
    {
        try
        {
            var icon = NativeMethods.WindowIcon(h);
            if (icon == IntPtr.Zero) return null;
            var bs = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());   // the window owns the handle
            bs.Freeze();
            return bs;
        }
        catch { return null; }
    }
}
