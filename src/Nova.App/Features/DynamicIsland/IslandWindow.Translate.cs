using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nova.App.Integrations.Windows;
using Nova.Core.Logging;
using Nova.Core.Translate;

namespace Nova.App.Features.DynamicIsland;

/// <summary>
/// "Terjemah" page: shortcut → type a text → Enter shows the translation in the notch (source language detected automatically).
/// The chips pick the target language, a click on a result copies it, Ctrl+Enter opens the same text in Google Translate in the browser.
/// </summary>
public partial class IslandWindow
{
    const int Translate = 7;

    readonly StackPanel _trLog = new();
    readonly List<Button> _trChips = new();
    ScrollViewer _trScroll = null!;
    TextBox _trInput = null!;
    string _trTarget = "id";
    CancellationTokenSource? _trCts;
    bool _trBusy;

    UIElement BuildTranslatePage()
    {
        var page = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        chips.Children.Add(Lbl("ke", 11, 0.7));
        ((TextBlock)chips.Children[0]).Margin = new Thickness(2, 0, 4, 0);
        ((TextBlock)chips.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        foreach (var (code, chip, name) in Translation.Languages)
        {
            var c = code;
            var b = TabBtn(chip, (_, _) => SetTranslateTarget(c, retranslate: true), $"Terjemahkan ke {name}");
            b.Padding = new Thickness(8, 3, 8, 3);
            _trChips.Add(b); chips.Children.Add(b);
        }
        var clear = IconBtn("\uE74D", (_, _) => { _trCts?.Cancel(); _trBusy = false; _trLog.Children.Clear(); _trInput.Focus(); }, "Hapus hasil terjemahan", 28);
        Grid.SetColumn(clear, 1);
        head.Children.Add(chips); head.Children.Add(clear);

        var hint = Lbl("Bahasa asal dikenali otomatis  ·  Enter terjemahkan  ·  klik v1 / v2 … untuk menyalin  ·  Ctrl+Enter buka Google Translate", 11, 0.6);
        hint.Margin = new Thickness(2, 6, 2, 4);
        _trScroll = new ScrollViewer
        {
            MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = _trLog
        };
        _trInput = MakeInput("Ketik teks yang mau diterjemahkan…", 450, "Teks untuk diterjemahkan", OnTranslateKey);
        _trInput.Margin = new Thickness(0, 6, 0, 0);

        page.Children.Add(head); page.Children.Add(hint); page.Children.Add(_trScroll); page.Children.Add(_trInput);
        page.Visibility = Visibility.Collapsed;
        PageHost.Children.Add(page);
        SetTranslateTarget(Translation.IsKnownLanguage(_s.TranslateTarget) ? _s.TranslateTarget : "id", retranslate: false);
        return page;
    }

    void SetTranslateTarget(string code, bool retranslate)
    {
        _trTarget = code;
        for (int i = 0; i < _trChips.Count; i++)
            _trChips[i].Tag = string.Equals(Translation.Languages[i].Code, code, StringComparison.OrdinalIgnoreCase) ? "on" : null;
        if (retranslate && _trInput.Text.Trim().Length > 0) _ = TranslateNowAsync(_trInput.Text.Trim());
        _trInput?.Focus();
    }

    void OnTranslateKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Collapse(); e.Handled = true; return; }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var text = _trInput.Text.Trim();
        if (text.Length == 0) return;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            try { Process.Start(new ProcessStartInfo(Translation.BuildBrowserUrl(text, _trTarget)) { UseShellExecute = true }); Collapse(); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            { FileLog.Write($"Translate could not open the browser: {ex.GetType().Name}"); AddTranslateBubble("Browser tidak bisa dibuka.", fromUser: false); }
            return;
        }
        _ = TranslateNowAsync(text);
    }

    async Task TranslateNowAsync(string text)
    {
        if (_trBusy) return;
        _trInput.Clear();
        AddTranslateBubble(text, fromUser: true);
        var bubble = AddTranslateBubble("…", fromUser: false);
        _trBusy = true;
        _trCts?.Cancel(); _trCts = new CancellationTokenSource();
        var (ok, versions, error) = await TranslateClient.TranslateAsync(text, _trTarget, _s.TranslateVersions, _trCts.Token);
        _trBusy = false;
        if (!ok) { ((TextBlock)bubble.Child).Text = "⚠ " + error; return; }

        // v1: …  v2: …  — every version is its own line; a click copies that one.
        var rows = new StackPanel();
        for (int i = 0; i < versions.Count; i++)
        {
            var v = versions[i];
            var row = Lbl("", 12.5);
            row.Margin = new Thickness(0, i == 0 ? 0 : 5, 0, 0);
            if (versions.Count > 1) row.Inlines.Add(new System.Windows.Documents.Run($"v{i + 1}: ") { FontWeight = FontWeights.SemiBold, Foreground = Brushes.Gray });
            row.Inlines.Add(new System.Windows.Documents.Run(v));
            row.Cursor = Cursors.Hand;
            row.ToolTip = "Klik untuk menyalin";
            AutomationProperties.SetName(row, $"Versi {i + 1}: {v}");
            row.MouseLeftButtonUp += (_, _) => { try { Clipboard.SetText(v); ShowNotice("Terjemahan disalin", seconds: 2); } catch (System.Runtime.InteropServices.ExternalException) { } };
            rows.Children.Add(row);
        }
        bubble.Child = rows;
        if (_s.TranslateVerbForms && VerbForms.TryFromTranslation(text, _trTarget, versions[0], out var verb))
            AddTranslateBubble(verb.Format(), fromUser: false);   // Verb 1 / Verb 2 / Verb 3 / Verb-ing / Verb-s
        Dispatcher.BeginInvoke(new Action(() => _trScroll.ScrollToEnd()), DispatcherPriority.Background);
    }

    Border AddTranslateBubble(string text, bool fromUser)
    {
        var tb = Lbl(text, 12.5);
        tb.Margin = new Thickness(0);
        var fill = (FindResource("NovaOnSurface") as SolidColorBrush)?.Clone() ?? new SolidColorBrush(Colors.Gray);
        fill.Opacity = fromUser ? 0.22 : 0.10;
        var bubble = new Border
        {
            Child = tb, Background = fill, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(fromUser ? 40 : 0, 3, fromUser ? 0 : 40, 3),
            HorizontalAlignment = fromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        AutomationProperties.SetName(bubble, fromUser ? "Teks asal" : "Terjemahan");
        _trLog.Children.Add(bubble);
        Dispatcher.BeginInvoke(new Action(() => _trScroll.ScrollToEnd()), DispatcherPriority.Background);
        return bubble;
    }

    /// <summary>Shortcut entry point: opens the Island on the Terjemah page with the cursor in the text box (again = close).</summary>
    public void InvokeTranslate()
    {
        InvokePage(Translate);
        Dispatcher.BeginInvoke(new Action(() => { if (_expanded && _pageIndex == Translate) _trInput.Focus(); }), DispatcherPriority.Input);
    }
}
