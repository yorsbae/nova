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
    TextBox? _profileName;

    public SettingsWindow(NovaSettings current, Func<NovaSettings, bool, bool> apply, Func<NovaSettings> resetAll)
    {
        _apply = apply; _resetAll = resetAll; _work = current.Clone();
        _liveWeather = current.WeatherEnabled; _liveClipboard = current.ClipboardHistoryEnabled; _liveLyrics = current.LyricsEnabled;
        InitializeComponent();
        BuildForm();
        Form.DataContext = _work;
        RefreshProfiles();
    }

    // ---- form construction (code-built so every control gets an explicit accessible name) ----
    void BuildForm()
    {
        Header("Layout");
        AddCombo<LayoutPreset>("Layout preset", nameof(NovaSettings.Preset));
        var applyPreset = SmallButton("Apply preset", "Apply the selected layout preset to the form");
        applyPreset.Click += (_, _) => { PresetApplier.Apply(_work, _work.Preset); Refresh(); Status($"Preset {_work.Preset} applied to the form. Press Apply to use it."); };
        Form.Children.Add(applyPreset);

        Header("Mac style");
        AddCheck("Mac-style notch: dock a bar to the top of the screen (windows never go under it) and attach the Island to it", nameof(NovaSettings.NotchMode));
        AddCheck("Replace the Windows taskbar and Start button with the dock (taskbar is hidden while NOVA runs and restored when it exits)", nameof(NovaSettings.HideTaskbar));
        AddCheck("Show open apps in the dock, with a dot under them", nameof(NovaSettings.ShowRunningApps));
        AddCheck("Use Windows dark mode while NOVA runs (restored when NOVA exits)", nameof(NovaSettings.WindowsDarkMode));
        AddCheck("Show the full top bar with date, Wi-Fi, sound and battery icons (off = only the Island, floating at the top centre)", nameof(NovaSettings.ShowTopBar));
        AddCheck("Island-only mode: keep maximized windows below the Island (off = the Island floats over the windows, like the concept)", nameof(NovaSettings.ReserveTopSpace));
        AddCheck("Give other apps' windows one look: same title-bar colour, light/dark mode, round corners (Windows 11; undone when off or on exit)", nameof(NovaSettings.HarmonizeWindows));
        AddCheck("Rounded-square app windows: large rounded corners on the programs you open (not maximized ones; edges are not anti-aliased; undone when off or on exit)", nameof(NovaSettings.RoundAppWindows));
        AddSlider("Corner radius of app windows", nameof(NovaSettings.WindowCornerRadius), 6, 40, 1, "0");
        AddCheck("Auto-hide the Windows taskbar (only when the dock does not replace it; restored when off or on exit)", nameof(NovaSettings.TaskbarAutoHide));
        AddCheck("Chat alerts on the Island (WhatsApp, Telegram, Discord...). Reads only window titles on this PC, not message text", nameof(NovaSettings.AppAlerts));

        Header("Appearance");
        AddCombo<ThemeMode>("Theme", nameof(NovaSettings.Theme));
        AddText("Accent color (#RRGGBB)", nameof(NovaSettings.AccentColor));
        AddSlider("Opacity (70%–100%)", nameof(NovaSettings.Opacity), 0.7, 1.0, 0.05, "P0");
        AddCheck("Increase contrast (use Windows system colors)", nameof(NovaSettings.HighContrastOverride));
        AddCheck("Use opaque windows if transparency looks wrong (restart NOVA to apply)", nameof(NovaSettings.OpaqueFallback));

        Header("Clock");
        AddCombo<ClockFormat>("Time format", nameof(NovaSettings.ClockFormat));
        AddSlider("Clock font size", nameof(NovaSettings.ClockFontSize), 12, 72, 1, "0");

        Header("Island");
        AddCombo<IslandBehavior>("Island behavior", nameof(NovaSettings.IslandBehavior));
        AddMonitor("Island monitor", nameof(NovaSettings.IslandMonitor));
        AddSlider("Island transparency: how much of the windows behind it shows through (100% = solid, only the background fades)", nameof(NovaSettings.IslandOpacity), 0.2, 1.0, 0.05, "P0");
        AddCheck("Dynamic Island opacity: keep the open Island readable (at least 60% solid); the compact Island follows the slider. Tip: hold Ctrl and scroll over the Island to change transparency", nameof(NovaSettings.IslandDynamicOpacity));
        AddSlider("Dock transparency: 20% = almost clear glass, 100% = solid (the see-through look needs transparent windows, not the solid fallback)", nameof(NovaSettings.DockOpacity), 0.2, 1.0, 0.05, "P0");
        AddSlider("Distance from top of screen (Island-only mode and classic floating mode; you can also drag the Island with the mouse)", nameof(NovaSettings.IslandTopOffset), 0, 2200, 1, "0");
        AddSlider("Sideways shift from the centre (negative = left, positive = right)", nameof(NovaSettings.IslandOffsetX), -2500, 2500, 1, "0");
        var resetIsland = SmallButton("Put the Island back at the top centre", "Reset the Island's position to the top centre of the screen");
        resetIsland.Click += (_, _) => { _work.IslandOffsetX = 0; _work.IslandTopOffset = new NovaSettings().IslandTopOffset; Refresh(); Status("Island position reset in the form. Press Apply to use it."); };
        Form.Children.Add(resetIsland);
        AddCombo<IslandFaceMode>("Compact (minimal) Island: clock only, clock and face taking turns, face only, or all modules taking turns (clock, face, water, prayer)", nameof(NovaSettings.IslandFaceMode));
        AddCombo<IslandFaceStyle>("Face style: Pet, Robot, Anime, Cat, Sleepy or Emoticon", nameof(NovaSettings.IslandFaceStyle));
        AddSlider("Seconds the clock stays before the face shows (clock-and-face mode)", nameof(NovaSettings.IslandFaceClockSeconds), 5, 120, 1, "0");
        AddSlider("Seconds the face stays before the clock returns", nameof(NovaSettings.IslandFaceShowSeconds), 3, 60, 1, "0");
        AddCheck("Show CPU and RAM use under the clock in the compact Island (read every 3 seconds while it is shown)", nameof(NovaSettings.IslandShowUsage));
        AddCheck("Live activities: show volume changes, charger, low battery and Bluetooth / Wi-Fi connecting on the Island", nameof(NovaSettings.ActivityAlerts));
        AddCheck("Eco mode: lower priority, Windows efficiency mode and memory trimming when idle (lightest on RAM and CPU)", nameof(NovaSettings.EcoMode));
        AddCheck("Expand the Island when the pointer rests on it, and fold it again when the pointer leaves", nameof(NovaSettings.IslandHoverExpand));
        AddCheck("File shelf: drop files on the Island to hold them for a moment, drag them out later", nameof(NovaSettings.ShelfEnabled));
        AddCheck("Show temporary notices (including 'now playing' in Adaptive mode)", nameof(NovaSettings.TemporaryNotices));
        AddText("Keyboard shortcut (e.g. Ctrl+Alt+N)", nameof(NovaSettings.Hotkey));

        Header("Dock");
        AddCheck("Auto-hide the dock: it slides away and comes back when the pointer touches the bottom edge of the screen", nameof(NovaSettings.DockAutoHide));
        AddMonitor("Dock monitor", nameof(NovaSettings.DockMonitor));
        AddSlider("Icon size", nameof(NovaSettings.DockIconSize), 24, 96, 1, "0");
        AddSlider("Icon spacing", nameof(NovaSettings.DockSpacing), 0, 32, 1, "0");
        AddCheck("Zoom icons towards the pointer, like the Mac Dock", nameof(NovaSettings.DockMagnify));
        AddSlider("Zoom size (1.0 = none)", nameof(NovaSettings.DockMagnifyScale), 1.0, 2.2, 0.1, "0.0");
        AddSlider("Distance from bottom of screen", nameof(NovaSettings.DockBottomOffset), 0, 200, 1, "0");
        AddCheck("Keep windows above the dock (reserve its strip of the screen, like the taskbar does)", nameof(NovaSettings.DockReservesSpace));
        AddCheck("Glass dock: see-through strip, only the Start button and app icons are visible", nameof(NovaSettings.DockGlass));

        Header("Information shown in the Island");
        AddCheck("Media controls for the app that is playing", nameof(NovaSettings.ShowMedia));
        AddCheck("System status: CPU, memory, battery (updates only while expanded)", nameof(NovaSettings.ShowSystemStatus));

        Header("Drink reminder (Island page 7)");
        AddCheck("Remind me to drink water with a notice on the Island", nameof(NovaSettings.HydrationEnabled));
        AddSlider("Minutes between reminders", nameof(NovaSettings.HydrationIntervalMinutes), 15, 240, 5, "0");
        AddSlider("Daily goal (glasses)", nameof(NovaSettings.HydrationGoalGlasses), 1, 20, 1, "0");
        AddSlider("Remind from (hour)", nameof(NovaSettings.HydrationFromHour), 0, 23, 1, "0");
        AddSlider("Remind until (hour)", nameof(NovaSettings.HydrationToHour), 0, 24, 1, "0");

        Header("Prayer times and adzan (Island page 8, worked out on this PC, no internet)");
        AddCheck("Show a notice (and play a sound) at each prayer time", nameof(NovaSettings.PrayerNotify));
        AddCheck("Play a sound with the adzan notice", nameof(NovaSettings.PrayerSound));
        var city = new ComboBox { ItemsSource = PrayerPlaces.All.Select(p => p.Name).Append(PrayerPlaces.Custom).ToList(), MinWidth = 200 };
        city.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(NovaSettings.PrayerCity)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(city, "Prayer times city");
        Row("City (times follow the Kemenag method: Fajr 20°, Isha 18°)", city);
        AddSlider("Latitude when City is Custom (negative = south)", nameof(NovaSettings.PrayerLatitude), -65, 65, 0.01, "0.00");
        AddSlider("Longitude when City is Custom (negative = west)", nameof(NovaSettings.PrayerLongitude), -180, 180, 0.01, "0.00");
        AddText("Adzan sound file (wav or mp3, empty = system sound)", nameof(NovaSettings.PrayerSoundPath));

        Header("Alarms (Calendar page, Alarm tab)");
        AddText("Alarm sound file (wav or mp3, empty = system sound)", nameof(NovaSettings.AlarmSoundPath));

        Header("Lyrics (optional, uses the internet)");
        AddCheck("Show synced lyrics. Sends the artist and song title (and length) of what is playing to lrclib.net", nameof(NovaSettings.LyricsEnabled));

        Header("Weather (optional, uses the internet)");
        AddCheck("Show weather. Sends your city (once) and its coordinates (every 30 min) to open-meteo.com", nameof(NovaSettings.WeatherEnabled));
        AddText("City", nameof(NovaSettings.WeatherCity));
        AddCheck("Use Fahrenheit", nameof(NovaSettings.WeatherFahrenheit));

        Header("Clipboard history (optional, private)");
        AddCheck("Keep clipboard history. Text, pictures and copied files; memory only, never saved to disk", nameof(NovaSettings.ClipboardHistoryEnabled));
        AddSlider("Items to keep", nameof(NovaSettings.ClipboardMaxItems), 5, 50, 1, "0");

        Header("Motion and startup");
        AddCheck("Reduce motion", nameof(NovaSettings.ReducedMotion));
        AddCheck("Start NOVA when I sign in to Windows", nameof(NovaSettings.StartWithWindows));

        Header("Layout profiles");
        _profileBox = new ComboBox { MinWidth = 200 };
        Row("Saved profiles", _profileBox);
        _profileName = new TextBox { MinWidth = 200, Padding = new Thickness(4) };
        Row("Profile name", _profileName);
        var profileButtons = new WrapPanel();
        var save = SmallButton("Save current layout as profile", "Save the current layout under the profile name");
        var load = SmallButton("Load selected profile", "Load the selected layout profile into the form");
        var del = SmallButton("Delete selected profile", "Delete the selected layout profile");
        save.Click += (_, _) => SaveProfile();
        load.Click += (_, _) => LoadProfile();
        del.Click += (_, _) => DeleteProfile();
        foreach (var b in new[] { save, load, del }) { b.Margin = new Thickness(0, 4, 8, 0); profileButtons.Children.Add(b); }
        Form.Children.Add(profileButtons);

        Header("Backup and restore");
        var backupButtons = new WrapPanel();
        var export = SmallButton("Export settings…", "Save the settings in the form to a backup file");
        var import = SmallButton("Import settings…", "Load settings from a backup file into the form");
        export.Click += (_, _) => ExportSettings();
        import.Click += (_, _) => ImportSettings();
        foreach (var b in new[] { export, import }) { b.Margin = new Thickness(0, 4, 8, 0); backupButtons.Children.Add(b); }
        Form.Children.Add(backupButtons);
    }

    Button SmallButton(string text, string accessibleName)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(b, accessibleName);
        return b;
    }

    void Header(string text)
    {
        var tb = new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 4) };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        AutomationProperties.SetName(tb, text);
        Form.Children.Add(tb);
    }

    void Row(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        Grid.SetColumn(control, 1);
        g.Children.Add(tb); g.Children.Add(control);
        AutomationProperties.SetName(control, label);
        Form.Children.Add(g);
    }

    void AddCombo<T>(string label, string prop) where T : struct, Enum
    {
        var cb = new ComboBox { ItemsSource = Enum.GetValues<T>(), MinWidth = 160 };
        cb.SetBinding(Selector.SelectedItemProperty, new Binding(prop) { Mode = BindingMode.TwoWay });
        Row(label, cb);
    }

    void AddMonitor(string label, string prop)
    {
        var cb = new ComboBox { ItemsSource = MonitorService.List(), DisplayMemberPath = nameof(MonitorChoice.Label), SelectedValuePath = nameof(MonitorChoice.DeviceName), MinWidth = 200 };
        cb.SetBinding(Selector.SelectedValueProperty, new Binding(prop) { Mode = BindingMode.TwoWay, FallbackValue = "" });
        Row(label, cb);
    }

    void AddText(string label, string prop)
    {
        var t = new TextBox { MinWidth = 160, Padding = new Thickness(4) };
        t.SetBinding(TextBox.TextProperty, new Binding(prop) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Row(label, t);
    }

    void AddCheck(string label, string prop)
    {
        var c = new CheckBox { Content = label, Margin = new Thickness(0, 4, 0, 4) };
        c.SetResourceReference(Control.ForegroundProperty, "NovaOnSurface");
        c.SetBinding(ToggleButton.IsCheckedProperty, new Binding(prop) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(c, label);
        Form.Children.Add(c);
    }

    void AddSlider(string label, string prop, double min, double max, double step, string format)
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
        Row(label, dock);
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
        if (!LayoutProfiles.TrySave(_work, _profileName?.Text, out var error)) { Status("Error: " + error); return; }
        RefreshProfiles();
        _profileBox!.SelectedItem = LayoutProfiles.CleanName(_profileName?.Text);
        Status($"Profile “{LayoutProfiles.CleanName(_profileName?.Text)}” saved. Press Apply to keep it permanently.");
    }

    void LoadProfile()
    {
        var name = _profileBox?.SelectedItem as string;
        if (name is null || !LayoutProfiles.TryApply(_work, name)) { Status("Error: choose a saved profile first."); return; }
        Refresh();
        Status($"Profile “{name}” loaded into the form. Press Apply to use it.");
    }

    void DeleteProfile()
    {
        var name = _profileBox?.SelectedItem as string;
        if (name is null || !LayoutProfiles.Delete(_work, name)) { Status("Error: choose a saved profile first."); return; }
        RefreshProfiles();
        Status($"Profile “{name}” deleted. Press Apply to keep this change.");
    }

    // ---- backup / restore ----
    void ExportSettings()
    {
        var dlg = new SaveFileDialog { Filter = "NOVA backup (*.json)|*.json", FileName = "nova-settings-backup.json", Title = "Export NOVA settings" };
        if (dlg.ShowDialog(this) != true) return;
        var error = SettingsStore.Export(dlg.FileName, _work);
        Status(error ?? "Settings exported.");
    }

    void ImportSettings()
    {
        var dlg = new OpenFileDialog { Filter = "NOVA backup (*.json)|*.json", Title = "Import NOVA settings" };
        if (dlg.ShowDialog(this) != true) return;
        if (!SettingsStore.TryImport(dlg.FileName, out var imported, out var error)) { Status("Error: " + error); return; }
        _work = imported!;
        _replaceDock = true;
        Refresh();
        RefreshProfiles();
        Status("Backup loaded into the form (including its dock shortcuts). Press Apply to use it.");
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
            Status("Error: the shortcut needs Ctrl, Alt, Shift or Win plus a key, for example Ctrl+Alt+N. Nothing was applied.");
            return false;
        }

        // Explicit consent for the two privacy-relevant features, asked when they are first switched on.
        if (_work.WeatherEnabled && !_liveWeather)
        {
            if (string.IsNullOrWhiteSpace(_work.WeatherCity)) { Status("Error: enter a city to use weather. Nothing was applied."); return false; }
            if (!Confirm("Weather uses the free open-meteo.com service.\n\nNOVA will send the city name you entered (once) and its coordinates (about every 30 minutes). Nothing else is sent.\n\nTurn on weather?"))
            { _work.WeatherEnabled = false; Refresh(); }
        }
        if (_work.LyricsEnabled && !_liveLyrics)
        {
            if (!Confirm("Lyrics use the free lrclib.net service.\n\nWhile a song plays, NOVA will send its artist, title and length to look up the lyrics. Nothing else is sent, and lyrics are only looked up for songs you play.\n\nTurn on lyrics?"))
            { _work.LyricsEnabled = false; Refresh(); }
        }
        if (_work.ClipboardHistoryEnabled && !_liveClipboard)
        {
            if (!Confirm("Clipboard history will remember the text, pictures and files you copy, in memory only, until you turn it off, clear it, or exit NOVA. It skips items that password managers mark as private. Anything you copy while it is on can appear in the list.\n\nTurn on clipboard history?"))
            { _work.ClipboardHistoryEnabled = false; Refresh(); }
        }

        if (!PresetApplier.Matches(_work, _work.Preset)) _work.Preset = LayoutPreset.Custom;

        var hotkeyOk = _apply(_work.Clone(), _replaceDock);
        _replaceDock = false;
        _liveWeather = _work.WeatherEnabled; _liveClipboard = _work.ClipboardHistoryEnabled; _liveLyrics = _work.LyricsEnabled;
        Refresh();
        Status(hotkeyOk
            ? "Settings applied."
            : $"Settings applied, but the shortcut {_work.Hotkey} is unavailable (another app may be using it). Choose a different one.");
        return true;
    }

    void OnApply(object sender, RoutedEventArgs e) => TryApply();
    void OnSaveClose(object sender, RoutedEventArgs e) { if (TryApply()) Close(); }
    void OnClose(object sender, RoutedEventArgs e) => Close();

    void OnResetAll(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Reset all settings, including dock shortcuts and saved layout profiles, to defaults?")) return;
        _work = _resetAll().Clone();
        _liveWeather = false; _liveClipboard = false; _liveLyrics = false; _replaceDock = false;
        Refresh();
        RefreshProfiles();
        Status("All settings were reset to defaults.");
    }
}
