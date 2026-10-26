using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nova.App.Integrations.Windows;
using Nova.Core.Clipboard;
using Nova.Core.Data;
using Nova.Core.Launch;
using Nova.Core.Logging;
using Nova.Core.Configuration;
using Nova.Core.Media;

namespace Nova.App.Features.DynamicIsland;

/// <summary>The seven pages of the expanded Island and the way you move between them.</summary>
public partial class IslandWindow
{
    static readonly string[] PageTitles =
    {
        "Wi-Fi, Suara & Pengaturan", "Musik", "Catatan & Clipboard",
        "Pomodoro", "Kalender & Tugas",
        "Minum Air", "Waktu Sholat"
    };
    // Acara, tugas and alarm are ONE thing on the Kalender page (a task, with an optional time and alarm), so there are 7 pages.
    // The old "Sistem & Alat" page is gone: the RAM reading now sits on page 1.
    const int Quick = 0, Music = 1, Notes = 2, Pomo = 3, Calendar = 4, Water = 5, PrayerPage = 6;

    UIElement[] _pages = Array.Empty<UIElement>();
    readonly Ellipse[] _dots = new Ellipse[7];
    int _pageIndex, _sub;
    Point _swipeStart; bool _swipeArmed;

    readonly UserDataStore _dataStore = new();
    UserData _data = new();
    readonly PomodoroTimer _pomo = new();
    readonly DispatcherTimer _pomoTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _remTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer _quickTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    DateTime _calMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    DateTime _calDay = DateTime.Today;
    bool _quickBusy, _volSetting, _brightChecked, _brightSupported;
    ClipboardHistory? _clipSource;

    /// <summary>Clipboard history to show on page 3 (null when the feature is off).</summary>
    public ClipboardHistory? ClipboardSource
    {
        get => _clipSource;
        set
        {
            if (_clipSource is not null) _clipSource.Changed -= OnClipChanged;
            _clipSource = value;
            if (_clipSource is not null) _clipSource.Changed += OnClipChanged;
            if (_expanded && _pageIndex == Notes) RebuildClip();
        }
    }

    void OnClipChanged() => Dispatcher.InvokeAsync(() => { if (_expanded && _pageIndex == Notes && _sub == 1) RebuildClip(); });

    void InitPages()
    {
        BuildCalendarTabs();
        var water = BuildWaterPage();
        var prayer = BuildPrayerPage();
        water.Visibility = Visibility.Collapsed; prayer.Visibility = Visibility.Collapsed;
        PageHost.Children.Add(water);
        PageHost.Children.Add(prayer);
        _pages = new UIElement[]
        {
            PageQuick, WrapScrolling(PageMusic), WrapScrolling(PageNotes), WrapScrolling(PagePomo),
            WrapScrolling(PageCalendar), water, prayer
        };
        for (int i = 0; i < _dots.Length; i++)
        {
            var d = new Ellipse { Width = 5, Height = 5, Margin = new Thickness(2, 0, 2, 0), Opacity = 0.3 };
            d.SetResourceReference(Shape.FillProperty, "NovaOnSurface");
            _dots[i] = d;
            PageDots.Children.Add(d);
        }

        _data = _dataStore.Load();
        _pomoTimer.Tick += (_, _) => OnPomoTick();
        _remTimer.Tick += (_, _) => CheckReminders();
        _quickTimer.Tick += (_, _) => RefreshQuick();
        Closed += (_, _) => { _pomoTimer.Stop(); _remTimer.Stop(); _quickTimer.Stop(); _waveTimer.Stop(); _meter?.Dispose(); };
        UpdateRemTimer();

        // Swipe left/right with the mouse on a page, or scroll the wheel over the title.
        PageHost.PreviewMouseLeftButtonDown += OnSwipeDown;
        PageHost.PreviewMouseLeftButtonUp += OnSwipeUp;
        if (PageTitle.Parent is UIElement header) header.MouseWheel += (_, e) => { GoToPage(_pageIndex + (e.Delta < 0 ? 1 : -1)); e.Handled = true; };

        GoToPage(Quick, morph: false);
    }

    // ---- navigation ----

    void GoToPage(int index, bool morph = true)
    {
        index = (index % _pages.Length + _pages.Length) % _pages.Length;
        if (_radioOpen && index != Quick) CloseRadio(morph: false);
        var from = CurrentSize();
        FitToQuickPage(index);
        _pageIndex = index;
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            _dots[i].Opacity = i == index ? 1 : 0.3;
        }
        PageTitle.Text = PageTitles[index];
        PageCounter.Content = $"{index + 1}/{_pages.Length}";
        RefreshPage();
        UpdateQuickTimer();
        UpdateWave();
        if (morph && _expanded) Morph(from);
    }

    /// <summary>The page that matters most right now: the running timer, else the music, else the quick settings.</summary>
    void OpenAdaptivePage()
    {
        int target = _forcePage ?? (_pomo.Running ? Pomo : _s.ShowMedia && _media is { HasTrack: true, IsPlaying: true } ? Music : Quick);
        _forcePage = null;
        GoToPage(target, morph: false);
    }

    void RefreshPage()
    {
        switch (_pageIndex)
        {
            case Quick: ApplyQuickBasics(); RefreshQuick(); RefreshRam(); break;
            case Notes: SetSub(_sub, morph: false); break;
            case Pomo: UpdatePomoUi(); break;
            case Calendar: SetCalView(_calView, morph: false); break;
            case Water: RefreshWater(); break;
            case PrayerPage: RefreshPrayer(); break;
        }
    }

    void UpdateQuickTimer()
    {
        bool need = _expanded && _pageIndex == Quick && !_radioOpen;
        if (need && !_quickTimer.IsEnabled) _quickTimer.Start();
        else if (!need && _quickTimer.IsEnabled) _quickTimer.Stop();
    }

    void OnPagePrev(object sender, RoutedEventArgs e) => GoToPage(_pageIndex - 1);
    void OnPageNext(object sender, RoutedEventArgs e) => GoToPage(_pageIndex + 1);

    static bool IsInteractive(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is TextBoxBase or Slider or ButtonBase or ScrollBar) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    void OnSwipeDown(object sender, MouseButtonEventArgs e)
    {
        _swipeArmed = !IsInteractive(e.OriginalSource as DependencyObject);
        if (_swipeArmed) _swipeStart = e.GetPosition(this);
    }

    void OnSwipeUp(object sender, MouseButtonEventArgs e)
    {
        if (!_swipeArmed) return;
        _swipeArmed = false;
        var p = e.GetPosition(this);
        double dx = p.X - _swipeStart.X, dy = p.Y - _swipeStart.Y;
        if (Math.Abs(dx) < 70 || Math.Abs(dy) > 40) return;
        GoToPage(_pageIndex + (dx < 0 ? 1 : -1));
        e.Handled = true;   // a swipe is not a click on whatever is under the pointer
    }

    /// <summary>Left / Right arrow keys change page unless you are typing.</summary>
    bool HandlePageKey(KeyEventArgs e)
    {
        if (!_expanded || Keyboard.FocusedElement is TextBoxBase || Keyboard.FocusedElement is Slider) return false;
        // 1–7 jump straight to that page
        int digit = e.Key >= Key.D1 && e.Key <= Key.D7 ? e.Key - Key.D1 : e.Key >= Key.NumPad1 && e.Key <= Key.NumPad7 ? e.Key - Key.NumPad1 : -1;
        if (digit >= 0 && Keyboard.Modifiers == ModifierKeys.None && digit < _pages.Length) { GoToPage(digit); return true; }
        if (e.Key == Key.Left) { GoToPage(_pageIndex - 1); return true; }
        if (e.Key == Key.Right) { GoToPage(_pageIndex + 1); return true; }
        return false;
    }

    // ---- small builders ----

    static void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { FileLog.Write($"Couldn't open {uri}: {ex.GetType().Name}"); }
    }

    Border BuildRow(string text, string right = "", Action? onDelete = null, string deleteName = "Hapus",
                    string? check = null, Action? onCheck = null, bool strike = false, bool accent = false, Action? onClick = null, Brush? dot = null)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (check is not null)
        {
            var c = new Button { Style = (Style)FindResource("NovaIconButton"), Width = 26, Height = 26, FontSize = 15, Margin = new Thickness(0, 0, 4, 0), Content = check };
            AutomationProperties.SetName(c, strike ? "Tandai belum selesai" : "Tandai selesai");
            c.Click += (_, _) => onCheck?.Invoke();
            g.Children.Add(c);
        }
        if (dot is not null)
            g.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = dot, Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var t = new TextBlock
        {
            Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = text, TextDecorations = strike ? TextDecorations.Strikethrough : null, Opacity = strike ? 0.55 : 1
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, accent ? "NovaAccent" : "NovaOnSurface");
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        if (right.Length > 0)
        {
            var r = new TextBlock { Text = right, FontSize = 11, Opacity = 0.65, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            r.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
            Grid.SetColumn(r, 2);
            g.Children.Add(r);
        }
        if (onDelete is not null)
        {
            var d = new Button { Style = (Style)FindResource("NovaIconButton"), Width = 24, Height = 24, FontSize = 11, Margin = new Thickness(0), Content = "\uE711", ToolTip = deleteName };
            AutomationProperties.SetName(d, deleteName);
            d.Click += (_, _) => onDelete();
            Grid.SetColumn(d, 3);
            g.Children.Add(d);
        }
        var b = new Border { Child = g, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 4, 4), Margin = new Thickness(0, 3, 0, 0) };
        b.SetResourceReference(Border.BackgroundProperty, "NovaSurfaceAlt");
        if (onClick is not null) { b.Cursor = Cursors.Hand; b.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; }; }
        return b;
    }

    TextBlock Hint(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        return t;
    }

    void SaveData() { if (!_dataStore.Save(_data)) FileLog.Write("Couldn't save notes/plan/events."); }

    // ---- 1: Wi-Fi, sound, quick toggles ----

    void ApplyQuickBasics()
    {
        TglBluetooth.Visibility = _quick is { BluetoothPresent: false } ? Visibility.Collapsed : Visibility.Visible;
    }

    QuickStatus? _quick;

    /// <summary>CPU, memory, disk and network speed on page 1 (read every few seconds, only while page 1 is open).</summary>
    void RefreshRam()
    {
        if (!_expanded || _pageIndex != Quick || MetricsProvider is null) return;
        try
        {
            var m = MetricsProvider();
            SetMeter(CpuVal, CpuBar, m.Cpu);
            SetMeter(RamVal, RamBar, m.Memory);
            SetMeter(DiskVal, DiskBar, m.Disk);
            NetDown.Text = "↓ " + Nova.Core.Status.RateFormatter.Format(m.DownBytesPerSecond);
            NetUp.Text = "↑ " + Nova.Core.Status.RateFormatter.Format(m.UpBytesPerSecond);
        }
        catch { /* a reading that fails is skipped; the tiles keep their last value */ }
    }

    static void SetMeter(TextBlock value, Border bar, double? percent)
    {
        value.Text = percent is double p ? p.ToString("0", CultureInfo.CurrentCulture) + "%" : "–";
        if (bar.RenderTransform is ScaleTransform st) st.ScaleX = percent is double q ? Math.Clamp(q, 0, 100) / 100 : 0;
    }

    /// <summary>Height of page 1 as it rests (without the brightness slider), measured even while another page is showing.</summary>
    double MeasureQuickHeight()
    {
        var bright = BrightRow.Visibility;
        BrightRow.Visibility = Visibility.Collapsed;
        double width = PageHost.ActualWidth > 0 ? PageHost.ActualWidth : 356;
        QuickMain.Measure(new Size(width, double.PositiveInfinity));
        double h = QuickMain.DesiredSize.Height;
        BrightRow.Visibility = bright;
        return h;
    }

    /// <summary>
    /// Every page takes only the room its own content needs: no forced height and no minimum, so a short page leaves no empty space.
    /// (Long lists have their own maximum height and scroll inside it.)
    /// </summary>
    void FitToQuickPage(int index)
    {
        PageHost.ClearValue(FrameworkElement.MinHeightProperty);
        foreach (var p in _pages)
        {
            if (p is not FrameworkElement page) continue;
            page.ClearValue(FrameworkElement.HeightProperty);
            page.ClearValue(FrameworkElement.MaxHeightProperty);
        }
    }

    /// <summary>Puts a page in a scroll viewer (no visible bar until needed) so a fixed height never cuts its content off.</summary>
    ScrollViewer WrapScrolling(FrameworkElement page)
    {
        int at = PageHost.Children.IndexOf(page);
        if (at >= 0) PageHost.Children.RemoveAt(at);
        page.Visibility = Visibility.Visible;
        var sv = new ScrollViewer
        {
            Content = page, Visibility = Visibility.Collapsed, Focusable = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Top
        };
        if (at >= 0) PageHost.Children.Insert(at, sv); else PageHost.Children.Add(sv);
        return sv;
    }

    async void RefreshQuick()
    {
        if (_quickBusy || !_expanded) return;
        _quickBusy = true;
        try
        {
            var q = await Task.Run(QuickStatusService.ReadAsync);
            _quick = q;
            WifiChipGlyph.Text = q.Net == NetKind.Wired ? Nova.Core.Status.StatusGlyphs.Ethernet : Nova.Core.Status.StatusGlyphs.Wifi;
            WifiChipGlyph.Opacity = q.Net == NetKind.None ? 0.35 : q.Net == NetKind.Wifi ? Nova.Core.Status.StatusGlyphs.WifiOpacity(q.WifiSignal) : 1;
            WifiChipName.Text = q.Net switch { NetKind.Wifi => q.WifiName ?? "Wi-Fi", NetKind.Wired => "Kabel LAN", _ => "Offline" };
            WifiChipState.Text = q.Net == NetKind.None ? "Tidak terhubung" : q.Net == NetKind.Wifi && q.WifiSignal >= 0 ? $"Terhubung · {q.WifiSignal}%" : "Terhubung";
            BatteryChipGlyph.Text = q.BatteryPercent is int bp ? Nova.Core.Status.StatusGlyphs.Battery(bp, q.Charging) : "\uE83F";
            BatteryChipText.Text = q.BatteryPercent is int bp2 ? bp2 + "%" : "";
            ((FrameworkElement)BatteryChipText.Parent).Visibility = q.BatteryPercent is null ? Visibility.Collapsed : Visibility.Visible;
            ApplyQuickBasics();
            SetToggle(TglWifi, q.Net != NetKind.None);
            SetToggle(TglSound, !q.Muted && q.Volume is not null);
            SetToggle(TglBluetooth, q.BluetoothOn);
            SetToggle(TglNight, SystemToggles.NightLightOn() == true);
            SetToggle(TglDnd, SystemToggles.DndOn());
            SetToggle(TglAirplane, await RadioService.AirplaneOnAsync() == true);

            VolumeRow.Visibility = q.Volume is null ? Visibility.Collapsed : Visibility.Visible;
            if (q.Volume is double v && !VolumeSlider.IsMouseCaptureWithin)
            {
                _volSetting = true;
                VolumeSlider.Value = Math.Round(v * 100);
                _volSetting = false;
                ShowVolume(v, q.Muted);
            }
            RefreshRam();
        }
        catch (Exception ex) { FileLog.Write($"Quick page status failed: {ex.GetType().Name}"); }
        finally { _quickBusy = false; }
    }

    void ShowVolume(double v, bool muted)
    {
        VolumeIcon.Text = Nova.Core.Status.StatusGlyphs.Volume(v, muted);
        VolumeValue.Text = muted ? "Bisu" : Math.Round(v * 100) + "%";
    }

    void OnVolumeSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_volSetting) return;
        AudioService.SetVolume(e.NewValue / 100);
        ShowVolume(e.NewValue / 100, false);
    }

    /// <summary>Lit (accent) when the thing is on, as in the concept's round toggle buttons.</summary>
    void SetToggle(Button b, bool on)
    {
        if (on)
        {
            var c = (Application.Current.Resources["NovaAccent"] as SolidColorBrush)?.Color ?? Color.FromRgb(0x4C, 0x8D, 0xFF);
            c.A = 0x66;
            var br = new SolidColorBrush(c); br.Freeze();
            b.Background = br;
        }
        else b.ClearValue(Control.BackgroundProperty);
    }

    /// <summary>Airplane mode: one click switches every radio off (or back on), no Settings page.</summary>
    async void OnTglAirplane(object sender, RoutedEventArgs e)
    {
        TglAirplane.IsEnabled = false;
        try
        {
            bool turnOn = await RadioService.AirplaneOnAsync() != true;
            var result = await RadioService.SetAirplaneAsync(turnOn);
            if (!result.Ok) { if (result.Message is not null) ShowNotice(result.Message, important: true, seconds: 8); return; }
            SetToggle(TglAirplane, turnOn);
            ShowNotice(turnOn ? "Mode pesawat nyala" : "Mode pesawat mati", important: false, seconds: 3);
            await Task.Delay(1200);
            RefreshQuick();
        }
        finally { TglAirplane.IsEnabled = true; }
    }

    /// <summary>Do not disturb: one click, no Settings page.</summary>
    void OnTglDnd(object sender, RoutedEventArgs e)
    {
        var now = SystemToggles.ToggleDnd();
        if (now is null) { OpenUri("ms-settings:notifications"); return; }
        SetToggle(TglDnd, now.Value);
        ShowNotice(now.Value ? "Jangan ganggu nyala" : "Jangan ganggu mati", important: false, seconds: 3);
    }
    void OnTglSound(object sender, RoutedEventArgs e)
    {
        AudioService.ToggleMute();
        if (AudioService.TryGet(out var v, out var m)) { ShowVolume(v, m); SetToggle(TglSound, !m); }
    }

    // ---- 3: notes, clipboard, shelf ----

    void OnSubNotes(object sender, RoutedEventArgs e) => SetSub(0);
    void OnSubClip(object sender, RoutedEventArgs e) => SetSub(1);
    void OnSubShelf(object sender, RoutedEventArgs e) => SetSub(2);

    void SetSub(int sub, bool morph = true)
    {
        var from = CurrentSize();
        _sub = sub;
        NotesView.Visibility = sub == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClipView.Visibility = sub == 1 ? Visibility.Visible : Visibility.Collapsed;
        ShelfPage.Visibility = sub == 2 ? Visibility.Visible : Visibility.Collapsed;
        SubNotes.Tag = sub == 0 ? "on" : null;
        SubClip.Tag = sub == 1 ? "on" : null;
        SubShelf.Tag = sub == 2 ? "on" : null;
        if (sub == 0) RebuildNotes();
        if (sub == 1) RebuildClip();
        if (morph && _expanded) Morph(from);
    }

    /// <summary>A file is being dragged onto the notch: jump to the shelf.</summary>
    void ShowShelf()
    {
        if (!_s.ShelfEnabled) return;
        _sub = 2;
        GoToPage(Notes, morph: false);
        SetSub(2, morph: false);
    }

    NoteItem? _noteOpen;

    void RebuildNotes()
    {
        NotesList.Children.Clear();
        foreach (var n in _data.Notes.ToList())
        {
            var note = n;
            NotesList.Children.Add(BuildRow(ClipboardHistory.Preview(note.Text, 80), note.CreatedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture),
                dot: (Brush)FindResource("NovaAccent"), onClick: () => OpenNote(note),
                onDelete: () => { _data.Notes.Remove(note); SaveData(); RebuildNotes(); }, deleteName: "Hapus catatan"));
        }
        NotesList.Children.Add(Hint(_data.Notes.Count == 0 ? "Belum ada catatan. Ketik di atas lalu tekan Enter." : "Klik catatan untuk membaca isinya secara lengkap."));
    }

    void OnNoteAdd(object sender, RoutedEventArgs e)
    {
        if (!_data.AddNote(NoteInput.Text)) return;
        NoteInput.Clear();
        SaveData();
        RebuildNotes();
    }

    void OnNoteKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OnNoteAdd(sender, e); e.Handled = true; } }

    // The detail of one note: all of its text, with Copy, Save and Delete.

    void OpenNote(NoteItem note)
    {
        var from = CurrentSize();
        _noteOpen = note;
        NoteStamp.Text = note.CreatedUtc.ToLocalTime().ToString("dddd, d MMMM yyyy · HH:mm", CultureInfo.CurrentCulture);
        NoteEdit.Text = note.Text;
        NotesListView.Visibility = Visibility.Collapsed;
        NoteDetail.Visibility = Visibility.Visible;
        UpdateNoteEditState();
        if (_expanded) Morph(from);
        NoteEdit.Focus();
        NoteEdit.CaretIndex = NoteEdit.Text.Length;
    }

    void CloseNote()
    {
        var from = CurrentSize();
        _noteOpen = null;
        NoteDetail.Visibility = Visibility.Collapsed;
        NotesListView.Visibility = Visibility.Visible;
        RebuildNotes();
        if (_expanded) Morph(from);
    }

    bool NoteEdited() => _noteOpen is not null
        && UserData.Clean(NoteEdit.Text, UserData.MaxNoteLength) is { Length: > 0 } clean && clean != _noteOpen.Text;

    void UpdateNoteEditState()
    {
        NoteCount.Text = $"{NoteEdit.Text.Length}/{UserData.MaxNoteLength}";
        NoteSave.IsEnabled = NoteEdited();
    }

    void OnNoteEditChanged(object sender, TextChangedEventArgs e) { if (_noteOpen is not null) UpdateNoteEditState(); }

    void OnNoteBack(object sender, RoutedEventArgs e)
    {
        if (NoteEdited() && _data.UpdateNote(_noteOpen, NoteEdit.Text)) SaveData();   // going back never loses what was typed
        CloseNote();
    }

    void OnNoteSave(object sender, RoutedEventArgs e)
    {
        if (_noteOpen is null) return;
        if (_data.UpdateNote(_noteOpen, NoteEdit.Text)) SaveData();
        CloseNote();
    }

    void OnNoteCopy(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetText(NoteEdit.Text); }
        catch (Exception ex) { FileLog.Write($"Note copy failed: {ex.GetType().Name}"); }
    }

    void OnNoteDelete(object sender, RoutedEventArgs e)
    {
        if (_noteOpen is not null) { _data.Notes.Remove(_noteOpen); SaveData(); }
        CloseNote();
    }

    // ---- clipboard history on the same page ----

    /// <summary>Set by the app: turns clipboard history on (the same switch as in Settings). Null = the button is not offered.</summary>
    public Action? EnableClipboardRequested { get; set; }

    void OnClipEnable(object sender, RoutedEventArgs e) => EnableClipboardRequested?.Invoke();

    void OnClipClear(object sender, RoutedEventArgs e) => _clipSource?.Clear();

    void RebuildClip()
    {
        ClipList.Children.Clear();
        ClipClearButton.Visibility = Visibility.Collapsed;
        ClipEnableButton.Visibility = Visibility.Collapsed;
        if (_clipSource is null)
        {
            ClipHint.Text = "Riwayat clipboard belum aktif. Setelah diaktifkan, teks, gambar, dan file yang Anda salin muncul di sini. Hanya di memori, tidak pernah disimpan ke disk.";
            ClipEnableButton.Visibility = EnableClipboardRequested is null ? Visibility.Collapsed : Visibility.Visible;
            return;
        }
        var entries = _clipSource.Entries.ToList();
        ClipHint.Text = entries.Count == 0 ? "Belum ada yang disalin. Salin teks, gambar, atau file, lalu muncul di sini." : "Klik untuk menyalin lagi.";
        ClipClearButton.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var entry in entries) ClipList.Children.Add(BuildClipRow(entry));
    }

    Border BuildClipRow(ClipEntry entry)
    {
        void Copy()
        {
            if (ClipboardTools.Restore(entry)) ShowNotice("Disalin.", important: false);
            else FileLog.Write("Clipboard copy failed.");
        }
        void Delete() => _clipSource?.Remove(entry);
        var time = entry.CapturedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

        if (entry.Kind != ClipKind.Image)
            return BuildRow((entry.Kind == ClipKind.Files ? "\uD83D\uDCC4 " : "") + ClipboardHistory.Describe(entry, 70), time, onDelete: Delete, deleteName: "Hapus dari riwayat", onClick: Copy);

        // A picture: a small preview, its size, and the usual delete button.
        var row = BuildRow($"Gambar {entry.Width}×{entry.Height}", time, onDelete: Delete, deleteName: "Hapus dari riwayat", onClick: Copy);
        if (row.Child is Grid grid && ClipboardTools.Decode(entry.Png, 120) is { } thumb)
        {
            var preview = new System.Windows.Controls.Image { Source = thumb, Height = 44, MaxWidth = 88, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0) };
            RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);
            AutomationProperties.SetName(preview, $"Gambar {entry.Width} kali {entry.Height}");
            Grid.SetColumn(preview, 0);
            grid.Children.Add(preview);
        }
        return row;
    }

    // ---- 4: plan ----

    void RebuildPlan()
    {
        PlanList.Children.Clear();
        var today = _data.TodayTasks(DateTime.Today).ToList();
        int done = today.Count(t => t.Done), total = today.Count;
        PlanSummary.Text = total == 0 ? "Belum ada tugas hari ini." : $"Hari ini · {total} tugas · {done}/{total} selesai";
        PlanProgress.Value = _data.PlanProgress();
        foreach (var t in today.Take(5))
        {
            var task = t;
            PlanList.Children.Add(BuildRow((task.Priority > 0 ? "! " : "") + task.Title, task.Time,
                onDelete: () => { _data.Tasks.Remove(task); SaveData(); RebuildPlan(); }, deleteName: "Hapus tugas",
                check: task.Done ? "\uE73A" : "\uE739", onCheck: () => { task.Done = !task.Done; SaveData(); RebuildPlan(); },
                strike: task.Done, accent: task.Priority > 0 && !task.Done));
        }
        if (total > 5) PlanList.Children.Add(Hint($"+{total - 5} tugas lainnya"));
    }

    void OnPlanAdd(object sender, RoutedEventArgs e)
    {
        var title = (PlanInput.Text ?? "").Trim();
        int priority = title.EndsWith('!') ? 1 : 0;   // end a title with "!" to mark it important
        if (priority > 0) title = title.TrimEnd('!', ' ');
        if (!_data.AddTask(title, PlanTime.Text, priority)) return;
        PlanInput.Clear(); PlanTime.Clear();
        SaveData();
        RebuildPlan();
    }

    void OnPlanKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OnPlanAdd(sender, e); e.Handled = true; } }

    // ---- 5: pomodoro ----

    void UpdatePomoUi()
    {
        var now = DateTime.UtcNow;
        var rem = _pomo.Remaining(now);
        PomoPhase.Text = _pomo.Phase switch { PomodoroPhase.Focus => "Fokus", PomodoroPhase.ShortBreak => "Istirahat", _ => "Istirahat panjang" };
        PomoTime.Text = $"{(int)rem.TotalMinutes:00}:{rem.Seconds:00}";
        SetRing(1 - _pomo.Fraction(now));   // the red ring shows what is LEFT, like the concept
        var todo = _data.TodayTasks(DateTime.Today).FirstOrDefault(t => !t.Done);
        TodoTitle.Text = todo?.Title ?? "Tidak ada tugas";
        TodoTime.Text = todo?.Time ?? "";
        PomoToggle.Content = _pomo.Running ? "\uE769" : "\uE768";
        PomoSessions.Text = $"Sesi {_pomo.CompletedFocus}/{PomodoroTimer.FocusBeforeLongBreak}";
    }

    void SetRing(double fraction)
    {
        const double R = 34, C = 42;   // centre and radius inside the 84 x 84 box
        fraction = Math.Min(0.9995, Math.Max(0, fraction));
        if (fraction < 0.0005) { PomoArc.Data = Geometry.Empty; return; }
        double a = fraction * 2 * Math.PI;
        var fig = new PathFigure { StartPoint = new Point(C, C - R), IsClosed = false };
        fig.Segments.Add(new ArcSegment(new Point(C + R * Math.Sin(a), C - R * Math.Cos(a)), new Size(R, R), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry(new[] { fig });
        g.Freeze();
        PomoArc.Data = g;
    }

    void OnPomoToggle(object sender, RoutedEventArgs e)
    {
        _pomo.Toggle(DateTime.UtcNow);
        if (_pomo.Running) _pomoTimer.Start(); else _pomoTimer.Stop();
        UpdatePomoUi(); UpdateCompactText();
    }

    void OnPomoReset(object sender, RoutedEventArgs e) { _pomo.Reset(); _pomoTimer.Stop(); UpdatePomoUi(); UpdateCompactText(); }
    void OnPomoSkip(object sender, RoutedEventArgs e) { _pomo.Skip(); _pomoTimer.Stop(); UpdatePomoUi(); UpdateCompactText(); }

    void OnPomoTick()
    {
        if (_pomo.Tick(DateTime.UtcNow))
        {
            _pomoTimer.Stop();
            ShowNotice(_pomo.Phase == PomodoroPhase.Focus ? "Istirahat selesai. Siap fokus lagi?"
                : $"Sesi fokus selesai. Istirahat {(int)PomodoroTimer.LengthOf(_pomo.Phase).TotalMinutes} menit.", important: true, seconds: 12);
            if (!IsVisible) { Reposition(); Show(); }
        }
        if (_expanded && _pageIndex == Pomo) UpdatePomoUi();
        UpdateCompactText();
    }

    /// <summary>Text for the compact notch while the timer runs, or null.</summary>
    string? PomoCompactText()
    {
        if (!_pomo.Running) return null;
        var rem = _pomo.Remaining(DateTime.UtcNow);
        var label = _pomo.Phase == PomodoroPhase.Focus ? "Fokus" : "Istirahat";
        return $"{label}  {(int)rem.TotalMinutes:00}:{rem.Seconds:00}";
    }

    // ---- music page: the "enable lyrics" shortcut (there is only one music page) ----

    void UpdateLyricEnable()
    {
        bool has = _s.ShowMedia && _media is { HasTrack: true };
        LyricEnableButton.Visibility = has && !_s.LyricsEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Raised when the user taps "Aktifkan lirik" (the app turns the setting on and saves it).</summary>
    public event Action? EnableLyricsRequested;
    void OnEnableLyrics(object sender, RoutedEventArgs e) => EnableLyricsRequested?.Invoke();

    // ---- 6: calendar, plan and alarms in one page ----

    int _calView;   // kept for older call sites; the page has no tabs

    /// <summary>
    /// Acara, tugas and alarm are one function: a task with an optional time. One month view, one agenda for the chosen day,
    /// one add row: title, time, a bell (the alarm rings at that time) and optional weekday chips (repeat every week).
    /// </summary>
    void BuildCalendarTabs()
    {
        PageHost.Children.Remove(PagePlan);
        PagePlan.Visibility = Visibility.Collapsed;
        _alarmList = new StackPanel();   // not shown any more; alarms are listed in the agenda
        _alarmDaysRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 0; i < 7; i++)
        {
            var btn = TabBtn(DayInitials[i], (sender, _) => { var b = (Button)sender; b.Tag = b.Tag as string == "on" ? null : "on"; }, "Ulangi setiap hari " + DayNames[i]);
            btn.ToolTip = "Ulangi tiap " + DayNames[i] + " (butuh jam)";
            _alarmDays[i] = btn;
            _alarmDaysRow.Children.Add(btn);
        }
        PageCalendar.Children.Add(_alarmDaysRow);
    }

    StackPanel _alarmDaysRow = null!;

    /// <summary>Refreshes the merged page.</summary>
    void SetCalView(int view, bool morph)
    {
        var from = CurrentSize();
        RebuildCalendar(); RebuildAgenda();
        if (morph && _expanded) Morph(from);
    }

    void OnCalPrev(object sender, RoutedEventArgs e) { _calMonth = _calMonth.AddMonths(-1); RebuildCalendar(); }
    void OnCalNext(object sender, RoutedEventArgs e) { _calMonth = _calMonth.AddMonths(1); RebuildCalendar(); }

    void RebuildCalendar()
    {
        var culture = CultureInfo.CurrentCulture;
        CalTitle.Text = _calMonth.ToString("MMM yyyy", culture);
        CalGrid.Children.Clear();
        var first = culture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            var name = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)(((int)first + i) % 7));
            var h = new TextBlock { Text = name, FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 3) };
            h.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
            CalGrid.Children.Add(h);
        }
        int offset = ((int)_calMonth.DayOfWeek - (int)first + 7) % 7;
        int days = DateTime.DaysInMonth(_calMonth.Year, _calMonth.Month);
        int cells = (offset + days + 6) / 7 * 7;   // only as many weeks as the month needs
        for (int i = 0; i < cells; i++)
        {
            int day = i - offset + 1;
            if (day < 1 || day > days) { CalGrid.Children.Add(new Border { Height = 28 }); continue; }
            var date = new DateTime(_calMonth.Year, _calMonth.Month, day);
            bool hasEvent = _data.Events.Any(ev => ev.Start.Date == date) || _data.TasksOn(date).Any();
            bool isToday = date == DateTime.Today, isSelected = date == _calDay.Date;
            var label = new TextBlock
            {
                Text = day.ToString(culture), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = isToday || isSelected ? FontWeights.SemiBold : FontWeights.Normal
            };
            var circle = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, Child = label };
            if (isToday) { circle.SetResourceReference(Border.BackgroundProperty, "NovaAccent"); label.Foreground = Brushes.White; }
            else
            {
                label.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
                if (isSelected) { circle.BorderThickness = new Thickness(1.5); circle.SetResourceReference(Border.BorderBrushProperty, "NovaAccent"); }
            }
            var cell = new Grid();
            cell.Children.Add(circle);
            if (hasEvent)
            {
                var mark = new Ellipse { Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
                mark.SetResourceReference(Shape.FillProperty, "NovaAccent");
                cell.Children.Add(mark);
            }
            var btn = new Button { Style = (Style)FindResource("NovaBarButton"), Height = 28, Padding = new Thickness(0), Margin = new Thickness(0), Content = cell };
            AutomationProperties.SetName(btn, date.ToString("D", culture) + (hasEvent ? ", ada acara atau tugas" : ""));
            btn.Click += (_, _) => { _calDay = date; RebuildCalendar(); RebuildAgenda(); };
            CalGrid.Children.Add(btn);
        }
    }

    /// <summary>A day's tasks; for today also the undated ones and unfinished older ones, as on the plan tab.</summary>
    IEnumerable<PlanTask> DayTasks(DateTime day) => day.Date == DateTime.Today ? _data.TodayTasks(day) : _data.TasksOn(day);

    /// <summary>The chosen day: events, tasks and alarms in one list, in time order.</summary>
    void RebuildAgenda()
    {
        var culture = CultureInfo.CurrentCulture;
        AgendaTitle.Text = _calDay.ToString("dddd, d MMMM", culture);
        DayName.Text = _calDay.ToString("ddd", culture);
        DayNumber.Text = _calDay.Day.ToString(culture);
        DayMonth.Text = _calDay.ToString("MMM", culture);
        AgendaList.Children.Clear();
        var palette = new[] { "#FF7C5CFF", "#FF3DA5FF", "#FFFFB020", "#FF3DD68C" };
        int n = 0;
        var rows = new List<(string Key, UIElement Row)>();
        foreach (var ev in _data.EventsOn(_calDay).ToList())
        {
            var item = ev;
            var dot = (Brush)new BrushConverter().ConvertFromString(palette[n++ % palette.Length])!;
            rows.Add((item.Start.ToString("HH:mm", culture), BuildRow(item.Title, item.Start.ToString("HH:mm", culture), dot: dot,
                onDelete: () => { _data.Events.Remove(item); SaveData(); UpdateRemTimer(); RebuildAgenda(); RebuildCalendar(); }, deleteName: "Hapus acara")));
        }
        foreach (var t in DayTasks(_calDay).ToList())
        {
            var task = t;
            rows.Add((string.IsNullOrWhiteSpace(task.Time) ? "99:99" : task.Time, BuildRow((task.Alarm ? "⏰ " : "") + (task.Priority > 0 ? "! " : "") + task.Title, task.Time,
                onDelete: () => { _data.Tasks.Remove(task); SaveData(); UpdateExtraTimer(); RebuildAgenda(); RebuildCalendar(); }, deleteName: "Hapus tugas",
                check: task.Done ? "\uE73A" : "\uE739", onCheck: () => { task.Done = !task.Done; SaveData(); UpdateExtraTimer(); RebuildAgenda(); },
                strike: task.Done, accent: task.Priority > 0 && !task.Done)));
        }
        foreach (var a in _data.Alarms.Where(a => a.Days == 0 ? _calDay.Date == DateTime.Today : a.RepeatsOn(_calDay.DayOfWeek)).ToList())
        {
            var alarm = a;
            rows.Add((alarm.Time, BuildRow("⏰ " + (alarm.Label.Length > 0 ? alarm.Label : "Alarm"), alarm.Time + " · " + alarm.DaysText(),
                onDelete: () => { _data.Alarms.Remove(alarm); SaveData(); UpdateExtraTimer(); RebuildAgenda(); }, deleteName: "Hapus alarm",
                check: alarm.Enabled ? "\uE73A" : "\uE739",
                onCheck: () => { alarm.Enabled = !alarm.Enabled; if (alarm.Enabled) alarm.LastRang = null; SaveData(); UpdateExtraTimer(); RebuildAgenda(); },
                strike: !alarm.Enabled)));
        }
        foreach (var r in rows.OrderBy(r => r.Key, StringComparer.Ordinal).Take(6)) AgendaList.Children.Add(r.Row);
        if (rows.Count > 6) AgendaList.Children.Add(Hint($"+{rows.Count - 6} lainnya"));
        if (rows.Count == 0) AgendaList.Children.Add(Hint("Tidak ada tugas atau alarm."));
    }

    bool _addAlarm = true;   // the bell: the new task rings at its time

    /// <summary>The bell button: the new task rings (alarm) at its time, or not.</summary>
    void OnEventKind(object sender, RoutedEventArgs e)
    {
        _addAlarm = !_addAlarm;
        EventKind.Opacity = _addAlarm ? 1 : 0.4;
        EventKind.ToolTip = _addAlarm ? "Alarm nyala: berbunyi pada jam tugas. Klik untuk mematikan." : "Alarm mati: hanya catatan tugas. Klik untuk menyalakan.";
        AutomationProperties.SetName(EventKind, "Alarm untuk tugas ini: " + (_addAlarm ? "nyala" : "mati"));
    }

    /// <summary>One add function. No time = a plain task for the chosen day. With a time = a task that rings then (bell on).
    /// With weekday chips + a time = it repeats every week (listed as an alarm on those days).</summary>
    void OnEventAdd(object sender, RoutedEventArgs e)
    {
        var title = (EventInput.Text ?? "").Trim();
        int priority = title.EndsWith('!') ? 1 : 0;
        if (priority > 0) title = title.TrimEnd('!', ' ');
        int mask = 0;
        for (int i = 0; i < 7; i++) if (_alarmDays[i].Tag as string == "on") mask |= 1 << DayOrder[i];

        var timeText = (EventTime.Text ?? "").Trim();
        string time = "";
        if (timeText.Length > 0)
        {
            if (!TimeSpan.TryParse(timeText, CultureInfo.InvariantCulture, out var ts) || ts < TimeSpan.Zero || ts >= TimeSpan.FromDays(1))
            {
                ShowNotice("Jam tidak dikenali. Contoh: 14:30", important: true);
                return;
            }
            time = $"{ts.Hours:00}:{ts.Minutes:00}";
        }
        if (mask != 0 && time.Length == 0) { ShowNotice("Isi jam dulu untuk mengulang tiap minggu. Contoh: 06:30", important: true); return; }
        if (title.Length == 0 && time.Length == 0) return;

        if (mask != 0)
        {
            if (!_data.AddAlarm(time, title, mask)) { ShowNotice("Alarm sudah penuh.", important: true); return; }
        }
        else
        {
            if (title.Length == 0) title = "Alarm";
            if (!_data.AddTask(title, time, _calDay.Date, priority, alarm: _addAlarm && time.Length > 0))
            {
                ShowNotice("Daftar tugas sudah penuh.", important: true);
                return;
            }
        }
        EventInput.Clear(); EventTime.Clear();
        for (int i = 0; i < 7; i++) _alarmDays[i].Tag = null;
        SaveData(); UpdateExtraTimer();
        RebuildAgenda(); RebuildCalendar();
    }

    void OnEventKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OnEventAdd(sender, e); e.Handled = true; } }

    // Reminders: one tiny timer, and only while there are events at all.
    void UpdateRemTimer()
    {
        if (_data.Events.Count > 0) { if (!_remTimer.IsEnabled) { _remTimer.Start(); if (IsLoaded) CheckReminders(); } }
        else _remTimer.Stop();
    }

    void CheckReminders()
    {
        var due = _data.TakeDueReminders(DateTime.Now);
        if (due.Count == 0) return;
        var first = due[0];
        var text = $"{first.Start:HH:mm}  {first.Title}" + (due.Count > 1 ? $"  (+{due.Count - 1})" : "");
        ShowNotice("Pengingat: " + text, important: true, seconds: 12);
    }

    // ---- page 1 tools: night light, brightness ----

    /// <summary>Night light: one click switches it on or off (Windows' own setting, or NOVA's warm tint when Windows' layout is unknown). Never opens Settings.</summary>
    void OnToolNight(object sender, RoutedEventArgs e)
    {
        var now = SystemToggles.ToggleNightLight();
        if (now is null) { ShowNotice("Night light tidak bisa diubah di PC ini.", important: false, seconds: 4); return; }
        SetToggle(TglNight, now.Value);
        ShowNotice(now.Value ? "Night light nyala" : "Night light mati", important: false, seconds: 3);
    }

    /// <summary>The brightness button: shows or hides the slider right on page 1 (laptops), or opens Windows display settings when the screen has no software brightness.</summary>
    async void OnToolBright(object sender, RoutedEventArgs e)
    {
        _brightProbe ??= ProbeBrightness();
        await _brightProbe;
        if (!_brightSupported) { OpenUri("ms-settings:display"); return; }
        var from = CurrentSize();
        BrightRow.Visibility = BrightRow.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (BrightRow.Visibility == Visibility.Visible) BrightSlider.Focus();
        if (_expanded) Morph(from);
    }

    // ---- music visualizer: the real output level of the speakers, drawn as a moving waveform ----

    const int WaveBars = 26;
    readonly DispatcherTimer _waveTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    AudioService.Meter? _meter;
    readonly double[] _waveLevels = new double[WaveBars];
    Rectangle[]? _waveRects;

    /// <summary>Runs only while the Music page is open, a song is playing and the Island is expanded.</summary>
    void UpdateWave()
    {
        bool need = _expanded && _pageIndex == Music && _s.ShowMedia && _media is { HasTrack: true, IsPlaying: true } && !ReducedMotion;
        if (need)
        {
            if (_waveRects is null)
            {
                _waveRects = new Rectangle[WaveBars];
                for (int i = 0; i < WaveBars; i++)
                {
                    var r = new Rectangle { Width = 3, Height = 3, Margin = new Thickness(1, 0, 1, 0), RadiusX = 1.5, RadiusY = 1.5, VerticalAlignment = VerticalAlignment.Bottom };
                    r.SetResourceReference(Shape.FillProperty, "NovaAccent");
                    _waveRects[i] = r;
                    Wave.Children.Add(r);
                }
                _waveTimer.Tick += (_, _) => TickWave();
            }
            if (_meter is null) { var m = new AudioService.Meter(); if (m.Start()) _meter = m; else m.Dispose(); }
            if (_meter is not null && !_waveTimer.IsEnabled) _waveTimer.Start();
        }
        else
        {
            _waveTimer.Stop();
            _meter?.Dispose(); _meter = null;   // nothing is held open while the visualizer is hidden
        }
    }

    void TickWave()
    {
        if (_meter is null || _waveRects is null) return;
        Array.Copy(_waveLevels, 1, _waveLevels, 0, WaveBars - 1);
        _waveLevels[WaveBars - 1] = _meter.Peak();
        for (int i = 0; i < WaveBars; i++) _waveRects[i].Height = 3 + _waveLevels[i] * 28;
    }

    static string? RunPowerShell(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"" + command + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(4000)) { try { p.Kill(); } catch { } return null; }
            return output.Trim();
        }
        catch { return null; }
    }

    Task? _brightProbe;

    /// <summary>Screen brightness exists only on laptops and tablets with a built-in display; elsewhere the button opens Windows display settings.</summary>
    async Task ProbeBrightness()
    {
        _brightChecked = true;
        var text = await Task.Run(() => RunPowerShell("(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness).CurrentBrightness"));
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 100)
        {
            _brightSupported = true;
            BrightSlider.Value = Math.Max(5, value);
        }
    }

    void OnBrightCommit(object sender, MouseButtonEventArgs e) => Dispatcher.InvokeAsync(ApplyBrightness, DispatcherPriority.Background);
    void OnBrightKeyCommit(object sender, KeyEventArgs e) => ApplyBrightness();

    void ApplyBrightness()
    {
        int v = (int)Math.Round(Math.Min(100, Math.Max(5, BrightSlider.Value)));   // an integer: nothing else ever reaches the command
        _ = Task.Run(() => RunPowerShell($"(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods).WmiSetBrightness(1,{v})"));
    }
}
