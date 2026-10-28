using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.Win32;
using Nova.App.Integrations.Windows;
using Nova.Core.Configuration;
using Nova.Core.Reminders;

namespace Nova.App.Settings;

public partial class SettingsWindow : Window
{
    /// <summary>Applies settings. Second argument: true if the dock list in the form should REPLACE the live one (after an import).</summary>
    readonly Func<NovaSettings, bool, bool> _apply;   // returns false if the hotkey could not be registered
    readonly Func<NovaSettings> _resetAll;
    NovaSettings _work;
    bool _liveWeather, _liveClipboard, _liveLyrics, _replaceDock;
    ComboBox? _profileBox;
    StackPanel _cur = new();   // the page being built (one page per menu item)
    readonly List<(RadioButton Nav, StackPanel Page)> _pages = new();
    TextBox? _profileName;

    public SettingsWindow(NovaSettings current, Func<NovaSettings, bool, bool> apply, Func<NovaSettings> resetAll)
    {
        _apply = apply; _resetAll = resetAll; _work = current.Clone();
        _liveWeather = current.WeatherEnabled; _liveClipboard = current.ClipboardHistoryEnabled; _liveLyrics = current.LyricsEnabled;
        InitializeComponent();
        BuildForm();
        Form.DataContext = _work;
        if (_pages.Count > 0) _pages[0].Nav.IsChecked = true;
        RefreshProfiles();
    }

    // ---- form construction (code-built so every control gets an explicit accessible name) ----
    // Each row is a short label; whatever needs explaining goes in a muted hint line under it.
    void BuildForm()
    {
        Category("Tampilan");
        Header("Tata letak");
        AddCombo<LayoutPreset>("Preset tata letak", nameof(NovaSettings.Preset));
        var applyPreset = SmallButton("Terapkan preset ke formulir", "Terapkan preset tata letak yang dipilih ke formulir");
        applyPreset.Click += (_, _) => { PresetApplier.Apply(_work, _work.Preset); Refresh(); Status($"Preset {_work.Preset} masuk ke formulir. Tekan Terapkan untuk memakainya."); };
        _cur.Children.Add(applyPreset);

        Header("Warna dan tema");
        AddCombo<ThemeMode>("Tema", nameof(NovaSettings.Theme));
        AddText("Warna aksen", nameof(NovaSettings.AccentColor), "Format #RRGGBB, misalnya #4C8DFF.");
        AddSlider("Kepekatan jendela", nameof(NovaSettings.Opacity), 0.7, 1.0, 0.05, "P0");
        AddCheck("Kontras tinggi", nameof(NovaSettings.HighContrastOverride), "Memakai warna sistem Windows.");
        AddCheck("Jendela tanpa transparansi", nameof(NovaSettings.OpaqueFallback), "Nyalakan kalau transparansi tampak salah. Mulai ulang NOVA agar berlaku.");

        Header("Jam");
        AddCombo<ClockFormat>("Format waktu", nameof(NovaSettings.ClockFormat));
        AddSlider("Ukuran huruf jam", nameof(NovaSettings.ClockFontSize), 12, 72, 1, "0");

        Category("Bilah atas");
        Header("Notch dan taskbar");
        AddCheck("Notch ala Mac", nameof(NovaSettings.NotchMode), "Bilah menempel di tepi atas layar dan Island menyatu di tengahnya. Jendela tidak masuk ke bawahnya.");
        AddCheck("Ganti taskbar dengan dock", nameof(NovaSettings.HideTaskbar), "Taskbar dan tombol Start disembunyikan selama NOVA jalan, lalu dikembalikan saat NOVA ditutup.");
        AddCheck("Sembunyikan taskbar otomatis", nameof(NovaSettings.TaskbarAutoHide), "Hanya bila dock tidak menggantikan taskbar.");
        AddCheck("Tampilkan aplikasi yang terbuka di dock", nameof(NovaSettings.ShowRunningApps), "Ditandai titik di bawah ikon.");
        AddCheck("Bilah atas penuh", nameof(NovaSettings.ShowTopBar), "Tanggal, Wi-Fi, suara, dan baterai. Kalau mati, hanya Island yang tampil di tengah atas.");
        AddCheck("Mode Island saja", nameof(NovaSettings.ReserveTopSpace), "Jendela maksimal berhenti di bawah Island. Kalau mati, Island melayang di atas jendela.");

        Header("Tampilan jendela lain");
        AddCheck("Mode gelap Windows selama NOVA jalan", nameof(NovaSettings.WindowsDarkMode), "Dikembalikan saat NOVA ditutup.");
        AddCheck("Samakan tampilan jendela aplikasi lain", nameof(NovaSettings.HarmonizeWindows), "Warna bilah judul, mode terang/gelap, dan sudut membulat (Windows 11). Dibatalkan saat dimatikan atau NOVA ditutup.");
        AddCheck("Sudut jendela membulat besar", nameof(NovaSettings.RoundAppWindows), "Tidak berlaku untuk jendela maksimal. Tepinya tidak halus (tanpa anti-aliasing).");
        AddSlider("Radius sudut jendela", nameof(NovaSettings.WindowCornerRadius), 6, 40, 1, "0");

        Category("Island");
        Header("Perilaku");
        AddCombo<IslandBehavior>("Perilaku Island", nameof(NovaSettings.IslandBehavior));
        AddCheck("Buka saat kursor berhenti di atasnya", nameof(NovaSettings.IslandHoverExpand), "Island melipat lagi saat kursor pergi.");
        AddCheck("Pemberitahuan sementara", nameof(NovaSettings.TemporaryNotices), "Termasuk lagu yang sedang diputar pada mode Adaptif.");
        AddCheck("Notifikasi aplikasi chat", nameof(NovaSettings.AppAlerts), "Pengirim dan isi pesan WhatsApp, Telegram, Discord, dan lainnya, dibaca dari notifikasi Windows di PC ini. Butuh izin notifikasi Windows.");
        AddCheck("Aktivitas langsung", nameof(NovaSettings.ActivityAlerts), "Volume, pengisi daya, baterai lemah, serta Bluetooth dan Wi-Fi yang tersambung.");
        AddCheck("Rak berkas", nameof(NovaSettings.ShelfEnabled), "Jatuhkan berkas ke Island untuk disimpan sebentar, lalu seret keluar nanti.");

        Header("Posisi");
        AddMonitor("Monitor Island", nameof(NovaSettings.IslandMonitor));
        AddSlider("Jarak dari atas layar", nameof(NovaSettings.IslandTopOffset), 0, 2200, 1, "0", "Untuk mode Island saja dan mode melayang. Island juga bisa diseret dengan mouse.");
        AddSlider("Geser dari tengah", nameof(NovaSettings.IslandOffsetX), -2500, 2500, 1, "0", "Negatif ke kiri, positif ke kanan.");
        var resetIsland = SmallButton("Kembalikan ke tengah atas", "Kembalikan posisi Island ke tengah atas layar");
        resetIsland.Click += (_, _) => { _work.IslandOffsetX = 0; _work.IslandTopOffset = new NovaSettings().IslandTopOffset; Refresh(); Status("Posisi Island dikembalikan di formulir. Tekan Terapkan untuk memakainya."); };
        _cur.Children.Add(resetIsland);

        Header("Transparansi");
        AddSlider("Transparansi Island", nameof(NovaSettings.IslandOpacity), 0.2, 1.0, 0.05, "P0", "100% = pekat. Hanya latarnya yang memudar. Tahan Ctrl dan putar roda mouse di atas Island untuk mengubahnya cepat.");
        AddCheck("Jaga Island terbuka tetap terbaca", nameof(NovaSettings.IslandDynamicOpacity), "Island terbuka minimal 60% pekat. Island ringkas mengikuti slider.");

        Header("Isi Island");
        AddCheck("Kontrol media", nameof(NovaSettings.ShowMedia), "Untuk aplikasi yang sedang memutar.");
        AddCheck("Status sistem", nameof(NovaSettings.ShowSystemStatus), "CPU, memori, baterai. Diperbarui hanya saat Island terbuka.");
        AddCheck("CPU dan RAM di bawah jam", nameof(NovaSettings.IslandShowUsage), "Dibaca tiap 3 detik selama tampil di Island ringkas.");

        Category("Wajah Island");
        Header("Wajah");
        AddCombo<IslandFaceMode>("Isi Island ringkas", nameof(NovaSettings.IslandFaceMode), "Jam saja, jam dan wajah bergantian, wajah saja, atau semua modul bergantian (jam, wajah, air, salat).");
        AddCombo<IslandFaceStyle>("Gaya mata", nameof(NovaSettings.IslandFaceStyle), "Pet berupa mata bulat sederhana. Gaya lain berupa mata anime yang berkedip, melirik, menghilang, dan muncul lagi.");
        AddSlider("Detik jam tampil sebelum wajah", nameof(NovaSettings.IslandFaceClockSeconds), 5, 120, 1, "0", "Untuk mode jam dan wajah bergantian.");
        AddSlider("Detik wajah tampil sebelum jam kembali", nameof(NovaSettings.IslandFaceShowSeconds), 3, 60, 1, "0");

        Header("Perilaku mata");
        AddCheck("Mata mengikuti kursor", nameof(NovaSettings.IslandEyeFollowMouse), "Saat kursor diam, mata melirik sendiri.");
        AddCheck("Mata menunjukkan suasana", nameof(NovaSettings.IslandEyeMoods), "Mengantuk saat baterai lemah atau larut malam, menyipit saat fokus Pomodoro, melebar saat ada pemberitahuan, bercahaya saat musik.");
        AddCheck("Gaya mata mengikuti waktu", nameof(NovaSettings.IslandEyeAutoTheme), "Sore: AnimeAura. Malam: Hollow.");

        Category("Pintasan");
        Header("Island");
        AddText("Buka atau tutup Island", nameof(NovaSettings.Hotkey), "Contoh: Ctrl+Alt+N.");
        AddText("Sembunyikan atau tampilkan Island", nameof(NovaSettings.ToggleHotkey), "Kosong = mati. Saat tersembunyi, hanya pemberitahuan penting seperti alarm yang muncul.");
        AddText("Awalan pintasan halaman", nameof(NovaSettings.PageHotkeyPrefix), "Awalan + 1 sampai 7 membuka halaman Island itu. Ctrl+Alt menghasilkan Ctrl+Alt+1 sampai Ctrl+Alt+7. Kosong = mati.");
        Header("Alat cepat");
        AddText("Peluncur cepat", nameof(NovaSettings.LauncherHotkey), "Ketik untuk membuka aplikasi, folder, atau alamat web, atau hitung angka. Contoh: Ctrl+Alt+Space. Kosong = mati.");
        AddText("Terjemah", nameof(NovaSettings.TranslateHotkey), "Contoh: Ctrl+Alt+T. Kosong = mati.");
        AddText("Tanya", nameof(NovaSettings.AskHotkey), "Contoh: Ctrl+Alt+K. Kosong = mati.");

        Category("Tanya dan Terjemah");
        Header("Tanya");
        AddChoice("Mesin bawaan", nameof(NovaSettings.AskEngine), new[]
        {
            ("googleai", "Google AI Mode (browser)"), ("google", "Google (browser)"), ("brave", "Brave (browser)"),
            ("aihere", "AI di sini (jawaban di notch, tanpa key)"), ("gemini", "Gemini (jawaban di notch, butuh key)"),
        });
        AddText("Gaya jawaban", nameof(NovaSettings.AskStyle), "Instruksi bebas, mis. \"Jawab maksimal 3 kalimat, Bahasa Indonesia santai\". Dipakai oleh Gemini dan AI di sini.");
        AddSlider("Panjang jawaban maksimal", nameof(NovaSettings.AskMaxChars), 100, 3000, 100, "0", "Dalam karakter, di notch.");
        AddText("Kunci API Gemini", nameof(NovaSettings.GeminiApiKey), "Dari aistudio.google.com. Kosong = mati. Pertanyaan dikirim ke Google hanya pada mode Gemini.");
        Header("Terjemah");
        AddChoice("Bahasa tujuan awal", nameof(NovaSettings.TranslateTarget), new[]
        {
            ("id", "Indonesia"), ("en", "Inggris"), ("ja", "Jepang"), ("ar", "Arab"), ("zh-CN", "Mandarin"), ("ko", "Korea"),
        });
        AddSlider("Jumlah versi terjemahan", nameof(NovaSettings.TranslateVersions), 1, 5, 1, "0", "Ditampilkan sebagai v1, v2, dan seterusnya.");
        AddCheck("Bentuk kata kerja", nameof(NovaSettings.TranslateVerbForms), "Menampilkan Verb 1, Verb 2, Verb 3, Verb-ing, dan Verb-s di bawah hasil bila yang diterjemahkan satu kata kerja.");

        Category("Dock");
        Header("Perilaku");
        AddCheck("Sembunyikan dock otomatis", nameof(NovaSettings.DockAutoHide), "Dock meluncur pergi dan kembali saat kursor menyentuh tepi bawah layar.");
        AddCheck("Sisihkan ruang untuk dock", nameof(NovaSettings.DockReservesSpace), "Jendela berhenti di atas dock, seperti taskbar.");
        AddMonitor("Monitor dock", nameof(NovaSettings.DockMonitor));
        Header("Ukuran dan posisi");
        AddSlider("Ukuran ikon", nameof(NovaSettings.DockIconSize), 24, 96, 1, "0");
        AddSlider("Jarak antar ikon", nameof(NovaSettings.DockSpacing), 0, 32, 1, "0");
        AddSlider("Jarak dari bawah layar", nameof(NovaSettings.DockBottomOffset), 0, 200, 1, "0");
        AddCheck("Perbesar ikon di dekat kursor", nameof(NovaSettings.DockMagnify), "Seperti Dock Mac.");
        AddSlider("Besar zoom", nameof(NovaSettings.DockMagnifyScale), 1.0, 2.2, 0.1, "0.0", "1.0 = tanpa zoom.");
        Header("Latar");
        AddCheck("Dock kaca", nameof(NovaSettings.DockGlass), "Latar tembus pandang. Hanya tombol Start dan ikon aplikasi yang terlihat.");
        AddSlider("Kepekatan latar dock", nameof(NovaSettings.DockOpacity), 0.2, 1.0, 0.05, "P0", "100% = pekat, 20% = sangat tembus pandang. Ikon tetap tajam.");

        Category("Pengingat");
        Header("Minum air (halaman Island 5)");
        AddCheck("Ingatkan minum lewat Island", nameof(NovaSettings.HydrationEnabled));
        AddSlider("Menit antar pengingat", nameof(NovaSettings.HydrationIntervalMinutes), 15, 240, 5, "0");
        AddSlider("Target harian (gelas)", nameof(NovaSettings.HydrationGoalGlasses), 1, 20, 1, "0");
        AddSlider("Mulai dari jam", nameof(NovaSettings.HydrationFromHour), 0, 23, 1, "0");
        AddSlider("Sampai jam", nameof(NovaSettings.HydrationToHour), 0, 24, 1, "0");

        Header("Waktu salat dan adzan (halaman Island 6)");
        AddCheck("Pemberitahuan tiap waktu salat", nameof(NovaSettings.PrayerNotify), "Dihitung di PC ini, tanpa internet.");
        AddCheck("Suara adzan", nameof(NovaSettings.PrayerSound));
        AddText("Berkas suara adzan", nameof(NovaSettings.PrayerSoundPath), "Berkas wav atau mp3. Kosong = suara sistem.");
        Row("Lokasi", new TextBlock { Text = "Otomatis dari lokasi Anda saat ini (desa, kabupaten, provinsi), dibaca dari Lokasi Windows. Nyalakan di Pengaturan > Privasi & keamanan > Lokasi. Metode Kemenag: Subuh 20 derajat, Isya 18 derajat.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });

        Header("Fokus, tugas, dan alarm (halaman Island 4)");
        AddText("Berkas suara alarm", nameof(NovaSettings.AlarmSoundPath), "Berkas wav atau mp3. Kosong = suara sistem.");

        Category("Online dan data");
        Header("Lirik");
        AddCheck("Lirik tersinkron", nameof(NovaSettings.LyricsEnabled), "Mengirim artis, judul, dan durasi lagu yang diputar ke lrclib.net.");
        Header("Cuaca");
        AddCheck("Cuaca di lokasi Anda", nameof(NovaSettings.WeatherEnabled), "Mengirim koordinat ke open-meteo.com tiap 30 menit.");
        AddCheck("Pakai Fahrenheit", nameof(NovaSettings.WeatherFahrenheit));
        Header("Riwayat clipboard");
        AddCheck("Simpan riwayat clipboard", nameof(NovaSettings.ClipboardHistoryEnabled), "Teks, gambar, dan berkas yang disalin. Hanya di memori, tidak pernah ditulis ke disk.");
        AddSlider("Jumlah item", nameof(NovaSettings.ClipboardMaxItems), 5, 50, 1, "0");

        Category("Hemat daya");
        Header("Baterai dan memori");
        AddCheck("Mode eco", nameof(NovaSettings.EcoMode), "Prioritas rendah, mode efisiensi Windows, dan pemangkasan memori saat idle.");
        AddCheck("Penghemat baterai", nameof(NovaSettings.BatterySaver), "Animasi (wajah, geser) dimatikan saat laptop memakai baterai.");
        Header("Gerak dan mulai");
        AddCheck("Kurangi gerak", nameof(NovaSettings.ReducedMotion));
        AddCheck("Jalankan NOVA saat masuk Windows", nameof(NovaSettings.StartWithWindows));

        Category("Cadangan");
        Header("Profil tata letak");
        _profileBox = new ComboBox { MinWidth = 200 };
        Row("Profil tersimpan", _profileBox);
        _profileName = new TextBox { MinWidth = 200, Padding = new Thickness(4) };
        Row("Nama profil", _profileName);
        var profileButtons = new WrapPanel();
        var save = SmallButton("Simpan tata letak ini", "Simpan tata letak saat ini dengan nama profil");
        var load = SmallButton("Muat profil", "Muat profil yang dipilih ke formulir");
        var del = SmallButton("Hapus profil", "Hapus profil yang dipilih");
        save.Click += (_, _) => SaveProfile();
        load.Click += (_, _) => LoadProfile();
        del.Click += (_, _) => DeleteProfile();
        foreach (var b in new[] { save, load, del }) { b.Margin = new Thickness(0, 4, 8, 0); profileButtons.Children.Add(b); }
        _cur.Children.Add(profileButtons);

        Header("Ekspor dan impor");
        var backupButtons = new WrapPanel();
        var export = SmallButton("Ekspor pengaturan…", "Simpan pengaturan di formulir ke berkas cadangan");
        var import = SmallButton("Impor pengaturan…", "Muat pengaturan dari berkas cadangan ke formulir");
        export.Click += (_, _) => ExportSettings();
        import.Click += (_, _) => ImportSettings();
        foreach (var b in new[] { export, import }) { b.Margin = new Thickness(0, 4, 8, 0); backupButtons.Children.Add(b); }
        _cur.Children.Add(backupButtons);
    }

    Button SmallButton(string text, string accessibleName)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(b, accessibleName);
        return b;
    }

    /// <summary>Starts a new page with its own button in the left menu.</summary>
    void Category(string title)
    {
        var page = new StackPanel { Margin = new Thickness(0, 0, 8, 12) };
        var big = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
        big.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        AutomationProperties.SetName(big, title);
        page.Children.Add(big);
        var nav = new RadioButton { GroupName = "SettingsMenu", Style = (Style)FindResource("NavItem"), Content = title };
        AutomationProperties.SetName(nav, title);
        nav.Checked += (_, _) => { Form.Content = page; PageScroll.ScrollToTop(); };
        Nav.Children.Add(nav);
        _pages.Add((nav, page));
        _cur = page;
    }

    void Header(string text)
    {
        var tb = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 6), Opacity = 0.7 };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        AutomationProperties.SetName(tb, text);
        _cur.Children.Add(tb);
    }

    void Hint(string? text, Thickness margin)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var h = new TextBlock { Text = text, FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = margin };
        h.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        AutomationProperties.SetName(h, text);
        _cur.Children.Add(h);
    }

    void Row(string label, UIElement control, string? hint = null)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        Grid.SetColumn(control, 1);
        g.Children.Add(tb); g.Children.Add(control);
        AutomationProperties.SetName(control, label);
        _cur.Children.Add(g);
        Hint(hint, new Thickness(210, -2, 0, 6));
    }

    void AddCombo<T>(string label, string prop, string? hint = null) where T : struct, Enum
    {
        var cb = new ComboBox { ItemsSource = Enum.GetValues<T>(), MinWidth = 160 };
        cb.SetBinding(Selector.SelectedItemProperty, new Binding(prop) { Mode = BindingMode.TwoWay });
        Row(label, cb, hint);
    }

    void AddMonitor(string label, string prop)
    {
        var cb = new ComboBox { ItemsSource = MonitorService.List(), DisplayMemberPath = nameof(MonitorChoice.Label), SelectedValuePath = nameof(MonitorChoice.DeviceName), MinWidth = 200 };
        cb.SetBinding(Selector.SelectedValueProperty, new Binding(prop) { Mode = BindingMode.TwoWay, FallbackValue = "" });
        Row(label, cb);
    }

    void AddText(string label, string prop, string? hint = null)
    {
        var t = new TextBox { MinWidth = 160, Padding = new Thickness(4) };
        t.SetBinding(TextBox.TextProperty, new Binding(prop) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Row(label, t, hint);
    }

    /// <summary>A pick-list for a setting stored as text (value kept in the file, label shown).</summary>
    void AddChoice(string label, string prop, (string Value, string Label)[] items, string? hint = null)
    {
        var cb = new ComboBox
        {
            ItemsSource = items.Select(i => new { i.Value, i.Label }).ToList(),
            DisplayMemberPath = "Label", SelectedValuePath = "Value", MinWidth = 220,
        };
        cb.SetBinding(Selector.SelectedValueProperty, new Binding(prop) { Mode = BindingMode.TwoWay });
        Row(label, cb, hint);
    }

    void AddCheck(string label, string prop, string? hint = null)
    {
        var c = new CheckBox { Content = label, Margin = new Thickness(0, 6, 0, hint is null ? 6 : 0) };
        c.SetResourceReference(Control.ForegroundProperty, "NovaOnSurface");
        c.SetBinding(ToggleButton.IsCheckedProperty, new Binding(prop) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(c, label);
        _cur.Children.Add(c);
        Hint(hint, new Thickness(22, 0, 0, 6));
    }

    void AddSlider(string label, string prop, double min, double max, double step, string format, string? hint = null)
    {
        var s = new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
        s.SetBinding(RangeBase.ValueProperty, new Binding(prop) { Mode = BindingMode.TwoWay });
        var value = new TextBlock { Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        value.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        value.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = s, StringFormat = "{0:" + format + "}" });
        AutomationProperties.SetName(value, label + " value");
        var dock = new DockPanel();
        DockPanel.SetDock(value, Dock.Right);
        dock.Children.Add(value);
        dock.Children.Add(s);
        AutomationProperties.SetName(s, label);
        Row(label, dock, hint);
        AutomationProperties.SetName(dock, label);
    }

    // ---- profiles ----
    void RefreshProfiles()
    {
        if (_profileBox is null) return;
        var previous = _profileBox.SelectedItem as string;
        _profileBox.ItemsSource = _work.Profiles.Select(p => p.Name).ToList();
        _profileBox.SelectedItem = previous is not null && _work.Profiles.Any(p => p.Name == previous) ? previous : _work.Profiles.FirstOrDefault()?.Name;
    }

    void SaveProfile()
    {
        if (!LayoutProfiles.TrySave(_work, _profileName?.Text, out var error)) { Status("Gagal: " + error); return; }
        RefreshProfiles();
        _profileBox!.SelectedItem = LayoutProfiles.CleanName(_profileName?.Text);
        Status($"Profil “{LayoutProfiles.CleanName(_profileName?.Text)}” tersimpan. Tekan Terapkan agar permanen.");
    }

    void LoadProfile()
    {
        var name = _profileBox?.SelectedItem as string;
        if (name is null || !LayoutProfiles.TryApply(_work, name)) { Status("Gagal: pilih profil tersimpan dulu."); return; }
        Refresh();
        Status($"Profil “{name}” dimuat ke formulir. Tekan Terapkan untuk memakainya.");
    }

    void DeleteProfile()
    {
        var name = _profileBox?.SelectedItem as string;
        if (name is null || !LayoutProfiles.Delete(_work, name)) { Status("Gagal: pilih profil tersimpan dulu."); return; }
        RefreshProfiles();
        Status($"Profil “{name}” dihapus. Tekan Terapkan agar perubahan ini tersimpan.");
    }

    // ---- backup / restore ----
    void ExportSettings()
    {
        var dlg = new SaveFileDialog { Filter = "Cadangan NOVA (*.json)|*.json", FileName = "nova-settings-backup.json", Title = "Ekspor pengaturan NOVA" };
        if (dlg.ShowDialog(this) != true) return;
        var error = SettingsStore.Export(dlg.FileName, _work);
        Status(error ?? "Pengaturan diekspor.");
    }

    void ImportSettings()
    {
        var dlg = new OpenFileDialog { Filter = "Cadangan NOVA (*.json)|*.json", Title = "Impor pengaturan NOVA" };
        if (dlg.ShowDialog(this) != true) return;
        if (!SettingsStore.TryImport(dlg.FileName, out var imported, out var error)) { Status("Gagal: " + error); return; }
        _work = imported!;
        _replaceDock = true;
        Refresh();
        RefreshProfiles();
        Status("Cadangan dimuat ke formulir, termasuk pintasan dock. Tekan Terapkan untuk memakainya.");
    }

    // ---- actions ----
    void Refresh() { Form.DataContext = null; Form.DataContext = _work; }

    void Status(string text)
    {
        StatusText.Text = text;
        var peer = UIElementAutomationPeer.FromElement(StatusText) ?? UIElementAutomationPeer.CreatePeerForElement(StatusText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    bool Confirm(string question) =>
        MessageBox.Show(this, question, "NOVA", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    bool TryApply()
    {
        if (!HotkeyParser.TryParse(_work.Hotkey, out var g) || !HotkeyService.TryResolveKey(g.Key, out _))
        {
            Status("Gagal: pintasan butuh Ctrl, Alt, Shift, atau Win ditambah satu tombol, misalnya Ctrl+Alt+N. Tidak ada yang diterapkan.");
            return false;
        }

        // Explicit consent for the two privacy-relevant features, asked when they are first switched on.
        if (_work.WeatherEnabled && !_liveWeather)
        {
            if (!Confirm("Cuaca memakai layanan gratis open-meteo.com.\n\nNOVA membaca lokasi Anda dan mengirim koordinatnya sekitar tiap 30 menit. Tidak ada data lain yang dikirim.\n\nNyalakan cuaca?"))
            { _work.WeatherEnabled = false; Refresh(); }
        }
        if (_work.LyricsEnabled && !_liveLyrics)
        {
            if (!Confirm("Lirik memakai layanan gratis lrclib.net.\n\nSaat lagu diputar, NOVA mengirim artis, judul, dan durasinya untuk mencari lirik. Tidak ada data lain yang dikirim.\n\nNyalakan lirik?"))
            { _work.LyricsEnabled = false; Refresh(); }
        }
        if (_work.ClipboardHistoryEnabled && !_liveClipboard)
        {
            if (!Confirm("Riwayat clipboard mengingat teks, gambar, dan berkas yang Anda salin, hanya di memori, sampai dimatikan, dihapus, atau NOVA ditutup. Item yang ditandai pribadi oleh pengelola kata sandi dilewati. Semua yang Anda salin selama ini menyala bisa muncul di daftar.\n\nNyalakan riwayat clipboard?"))
            { _work.ClipboardHistoryEnabled = false; Refresh(); }
        }

        if (!PresetApplier.Matches(_work, _work.Preset)) _work.Preset = LayoutPreset.Custom;

        var hotkeyOk = _apply(_work.Clone(), _replaceDock);
        _replaceDock = false;
        _liveWeather = _work.WeatherEnabled; _liveClipboard = _work.ClipboardHistoryEnabled; _liveLyrics = _work.LyricsEnabled;
        Refresh();
        Status(hotkeyOk
            ? "Pengaturan diterapkan."
            : $"Pengaturan diterapkan, tetapi pintasan {_work.Hotkey} tidak tersedia (mungkin dipakai aplikasi lain). Pilih yang lain.");
        return true;
    }

    void OnApply(object sender, RoutedEventArgs e) => TryApply();
    void OnSaveClose(object sender, RoutedEventArgs e) { if (TryApply()) Close(); }
    void OnClose(object sender, RoutedEventArgs e) => Close();

    void OnResetAll(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Kembalikan semua pengaturan ke bawaan, termasuk pintasan dock dan profil tata letak tersimpan?")) return;
        _work = _resetAll().Clone();
        _liveWeather = false; _liveClipboard = false; _liveLyrics = false; _replaceDock = false;
        Refresh();
        RefreshProfiles();
        Status("Semua pengaturan dikembalikan ke bawaan.");
    }
}
