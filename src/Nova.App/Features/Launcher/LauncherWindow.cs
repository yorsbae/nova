using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Nova.Core.Configuration;
using Nova.Core.Launch;
using Nova.Core.Logging;

namespace Nova.App.Features.Launcher;

/// <summary>
/// Quick launcher (shortcut, default Ctrl+Alt+Space): type a few letters to open a program, a pinned dock app or a common folder,
/// paste a web address to open it, or type a sum ("12*(3+4)") to get the answer (Enter copies it). Opened only on request, so it may take focus;
/// it closes on Esc, on Enter, or when you click elsewhere. Programs come from the Start menu shortcuts and the dock, read once in the
/// background and kept for a few minutes. Nothing is sent anywhere.
/// </summary>
public sealed class LauncherWindow : Window
{
    sealed record Hit(string Title, string Hint, Action Run);

    static List<LauncherEntry>? _cache;
    static DateTime _cacheAt;

    readonly List<DockItem> _dock;
    readonly Action<string, bool> _notify;
    readonly TextBox _input = new() { FontSize = 20, Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(0) };
    readonly ListBox _list = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, MaxHeight = 330, Margin = new Thickness(0, 6, 0, 0) };
    readonly List<Hit> _hits = new();
    List<LauncherEntry> _entries = new();
    bool _closing;

    public LauncherWindow(IEnumerable<DockItem> dockItems, Action<string, bool> notify)
    {
        _dock = dockItems.ToList(); _notify = notify;
        Title = "NOVA Launcher";
        AutomationProperties.SetName(this, "NOVA quick launcher");
        Width = 580; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Topmost = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = (SystemParameters.WorkArea.Width - Width) / 2 + SystemParameters.WorkArea.Left;
        Top = SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height * 0.22;
        SetResourceReference(BackgroundProperty, "NovaWindow");

        _input.SetResourceReference(ForegroundProperty, "NovaOnSurface");
        _input.SetResourceReference(BackgroundProperty, "NovaWindow");
        _input.CaretBrush = System.Windows.Media.Brushes.White;
        AutomationProperties.SetName(_input, "Type to search apps, folders, web addresses or a sum");
        AutomationProperties.SetName(_list, "Results");
        _list.SetResourceReference(ForegroundProperty, "NovaOnSurface");

        var hint = new TextBlock { Text = "Enter opens  ·  ↑ ↓ choose  ·  Esc closes  ·  type a sum like 12*(3+4) or paste a web address", FontSize = 11, Margin = new Thickness(10, 6, 10, 0), Opacity = 0.6 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");

        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(_input); panel.Children.Add(_list); panel.Children.Add(hint);
        Content = panel;

        _input.TextChanged += (_, _) => Search();
        _list.MouseDoubleClick += (_, _) => RunSelected();
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => CloseOnce();
        Loaded += (_, _) => { Activate(); _input.Focus(); };
        ContentRendered += (_, _) => { Activate(); _input.Focus(); };

        LoadIndex();
    }

    void CloseOnce() { if (_closing) return; _closing = true; Close(); }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: CloseOnce(); e.Handled = true; break;
            case Key.Down: Move(1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.Enter: RunSelected(); e.Handled = true; break;
        }
    }

    void Move(int step)
    {
        if (_hits.Count == 0) return;
        _list.SelectedIndex = (Math.Max(0, _list.SelectedIndex) + step + _hits.Count) % _hits.Count;
        _list.ScrollIntoView(_list.SelectedItem);
    }

    void RunSelected()
    {
        if (_hits.Count == 0) return;
        var hit = _hits[Math.Max(0, _list.SelectedIndex)];
        CloseOnce();
        try { hit.Run(); }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            FileLog.Write($"Launcher could not open an item: {ex.GetType().Name}");
            _notify("Couldn't open that.", true);
        }
    }

    void Search()
    {
        var q = _input.Text.Trim();
        _hits.Clear();
        if (q.Length > 0)
        {
            if (LauncherSearch.TryEvaluate(q, out var result))
                _hits.Add(new Hit("= " + result, "Press Enter to copy the answer", () => CopyText(result)));
            if (LauncherSearch.TryGetWebAddress(q, out var url))
                _hits.Add(new Hit("Open " + url, "Web address", () => Open(url)));
            if (Path.IsPathRooted(q) && Directory.Exists(q))
                _hits.Add(new Hit("Open folder " + q, "Folder", () => OpenFolder(q)));
            foreach (var e in LauncherSearch.Rank(_entries, q, 7))
            {
                var entry = e;
                _hits.Add(new Hit(entry.Name, Directory.Exists(entry.Path) ? "Folder" : entry.Path, () => Launch(entry.Path)));
            }
        }

        _list.Items.Clear();
        foreach (var h in _hits)
        {
            var row = new StackPanel { Margin = new Thickness(6, 3, 6, 3) };
            var title = new TextBlock { Text = h.Title, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
            var sub = new TextBlock { Text = h.Hint, FontSize = 11, Opacity = 0.55, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
            sub.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
            row.Children.Add(title); row.Children.Add(sub);
            _list.Items.Add(new ListBoxItem { Content = row });
        }
        _list.Visibility = _hits.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_hits.Count > 0) _list.SelectedIndex = 0;
    }

    // ---- what a result does ----

    void CopyText(string text)
    {
        try { Clipboard.SetText(text); _notify("Copied " + text, false); }
        catch (System.Runtime.InteropServices.COMException) { _notify("The clipboard is busy; try again.", true); }
    }

    static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    static void OpenFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    void Launch(string path)
    {
        if (Directory.Exists(path)) { OpenFolder(path); return; }
        if (!LaunchValidator.TryValidate(path, out var error)) { _notify(error ?? "Couldn't open that.", true); return; }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // ---- the list of things to find ----

    void LoadIndex()
    {
        if (_cache is not null && DateTime.UtcNow - _cacheAt < TimeSpan.FromMinutes(5)) { _entries = Merge(_cache); return; }
        _entries = Merge(new List<LauncherEntry>());   // dock apps and folders at once; the Start menu follows
        Task.Run(() => ReadStartMenu()).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) { _cache = t.Result; _cacheAt = DateTime.UtcNow; }
            Dispatcher.BeginInvoke(new Action(() => { _entries = Merge(_cache ?? new List<LauncherEntry>()); if (!_closing) Search(); }));
        }, TaskScheduler.Default);
    }

    List<LauncherEntry> Merge(List<LauncherEntry> startMenu)
    {
        var all = new List<LauncherEntry>();
        foreach (var d in _dock)
            if (!string.IsNullOrWhiteSpace(d.Name) && !string.IsNullOrWhiteSpace(d.Path)) all.Add(new LauncherEntry(d.Name, d.Path));
        all.AddRange(startMenu);
        foreach (var (name, path) in new[]
        {
            ("Task Manager", Path.Combine(Environment.SystemDirectory, "taskmgr.exe")),
            ("Notepad", Path.Combine(Environment.SystemDirectory, "notepad.exe")),
            ("Calculator", Path.Combine(Environment.SystemDirectory, "calc.exe")),
            ("Paint", Path.Combine(Environment.SystemDirectory, "mspaint.exe")),
        })
            if (File.Exists(path)) all.Add(new LauncherEntry(name, path));
        foreach (var (name, folder) in new[]
        {
            ("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        })
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) all.Add(new LauncherEntry(name, folder));
        return all;
    }

    /// <summary>The program shortcuts of the Start menu (all users and this user). Runs on a background thread; unreadable folders are skipped.</summary>
    static List<LauncherEntry> ReadStartMenu()
    {
        var list = new List<LauncherEntry>();
        foreach (var root in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
        })
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4 }))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new LauncherEntry(name, file));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { FileLog.Write($"Launcher: Start menu not readable ({ex.GetType().Name})"); }
        }
        return list;
    }
}
