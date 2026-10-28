using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nova.App.Integrations.Windows;
using Nova.Core.Ask;
using Nova.Core.Logging;

namespace Nova.App.Features.DynamicIsland;

/// <summary>
/// "Tanya" page: a question box with the conversation shown in the notch. Enter sends the question to the chosen engine:
/// Brave, Google or Google AI Mode open in the browser; Gemini answers right here (needs the person's own API key).
/// Tab switches engine; the prefixes "b ", "g ", "ai " and "? " pick one for a single question.
/// </summary>
public partial class IslandWindow
{
    const int Ask = 6;

    readonly StackPanel _askLog = new();
    readonly List<AskTurn> _askHistory = new();
    readonly List<Button> _askChips = new();
    ScrollViewer _askScroll = null!;
    TextBox _askInput = null!;
    TextBlock _askHint = null!;
    AskEngine _askEngine = AskEngine.GoogleAi;
    CancellationTokenSource? _askCts;
    bool _askBusy;

    UIElement BuildAskPage()
    {
        var page = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var engine in Enum.GetValues<AskEngine>())
        {
            var e = engine;
            var chip = TabBtn(AskSearch.Label(e), (_, _) => SetAskEngine(e), $"Kirim pertanyaan ke {AskSearch.Label(e)}");
            chip.Padding = new Thickness(8, 3, 8, 3);
            _askChips.Add(chip); chips.Children.Add(chip);
        }
        var clear = IconBtn("\uE74D", (_, _) => ClearAsk(), "Hapus percakapan", 28);
        Grid.SetColumn(clear, 1);
        head.Children.Add(chips); head.Children.Add(clear);

        _askHint = Lbl("", 11, 0.6);
        _askHint.Margin = new Thickness(2, 6, 2, 4);
        _askScroll = new ScrollViewer
        {
            MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = _askLog
        };
        _askInput = MakeInput("Tanya apa saja, lalu Enter…", 500, "Pertanyaan", OnAskKey);
        _askInput.Margin = new Thickness(0, 6, 0, 0);
        _askInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
            { SetAskEngine((AskEngine)(((int)_askEngine + 1) % Enum.GetValues<AskEngine>().Length)); e.Handled = true; }
        };

        page.Children.Add(head); page.Children.Add(_askHint); page.Children.Add(_askScroll); page.Children.Add(_askInput);
        page.Visibility = Visibility.Collapsed;
        PageHost.Children.Add(page);
        AskSearch.TryParseEngine(_s.AskEngine, out _askEngine);
        SetAskEngine(_askEngine);
        return page;
    }

    void SetAskEngine(AskEngine engine)
    {
        _askEngine = engine;
        var all = Enum.GetValues<AskEngine>();
        for (int i = 0; i < _askChips.Count; i++) _askChips[i].Tag = all[i] == engine ? "on" : null;
        _askHint.Text = engine == AskEngine.Gemini
            ? (_s.GeminiApiKey.Length == 0 ? "Gemini menjawab di sini, tapi butuh API key di Pengaturan  ·  Tab ganti mesin" : "Jawaban muncul di sini (pertanyaan dikirim ke Google Gemini)  ·  Tab ganti mesin")
            : $"Enter membuka {AskSearch.Label(engine)} di browser  ·  Tab ganti mesin  ·  awalan: b  g  ai  ?";
        _askInput?.Focus();
    }

    void OnAskKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SendAsk(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Collapse(); e.Handled = true; }
    }

    void SendAsk()
    {
        if (_askBusy) return;
        var (engine, query) = AskSearch.ParsePrefix(_askInput.Text, _askEngine);
        if (query.Length == 0) return;
        _askInput.Clear();
        AddAskBubble(query, fromUser: true, engine == AskEngine.Gemini ? null : $"↗ {AskSearch.Label(engine)}");
        if (engine == AskEngine.Gemini) { _ = AskGeminiAsync(query); return; }
        try
        {
            Process.Start(new ProcessStartInfo(AskSearch.BuildUrl(engine, query)) { UseShellExecute = true });
            Collapse();   // the browser is what the person wants to see now
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            FileLog.Write($"Ask could not open the browser: {ex.GetType().Name}");
            AddAskBubble("Browser tidak bisa dibuka.", fromUser: false);
        }
    }

    async Task AskGeminiAsync(string query)
    {
        _askHistory.Add(new AskTurn(true, query));
        var waiting = AddAskBubble("…", fromUser: false);
        _askBusy = true;
        _askCts?.Cancel(); _askCts = new CancellationTokenSource();
        var (ok, text) = await GeminiClient.AskAsync(_s.GeminiApiKey, _s.GeminiModel, _askHistory, _askCts.Token);
        _askBusy = false;
        if (ok) _askHistory.Add(new AskTurn(false, text)); else _askHistory.RemoveAt(_askHistory.Count - 1);
        waiting.Text = ok ? text : "⚠ " + text;
        ScrollAskToEnd();
    }

    TextBlock AddAskBubble(string text, bool fromUser, string? tag = null)
    {
        var tb = Lbl(tag is null ? text : $"{tag}  {text}", 12.5);
        tb.Margin = new Thickness(8, 5, 8, 5);
        var fill = (FindResource("NovaOnSurface") as SolidColorBrush)?.Clone() ?? new SolidColorBrush(Colors.Gray);
        fill.Opacity = fromUser ? 0.22 : 0.10;
        var bubble = new Border
        {
            Child = tb, Background = fill, CornerRadius = new CornerRadius(10), Margin = new Thickness(fromUser ? 40 : 0, 3, fromUser ? 0 : 40, 3),
            HorizontalAlignment = fromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        AutomationProperties.SetName(bubble, fromUser ? "Pertanyaan kamu" : "Jawaban");
        _askLog.Children.Add(bubble);
        ScrollAskToEnd();
        return tb;
    }

    void ScrollAskToEnd() => Dispatcher.BeginInvoke(new Action(() => _askScroll.ScrollToEnd()), DispatcherPriority.Background);

    void ClearAsk()
    {
        _askCts?.Cancel(); _askBusy = false;
        _askHistory.Clear(); _askLog.Children.Clear();
        _askInput.Focus();
    }

    /// <summary>Shortcut entry point: opens the Island on the Tanya page with the cursor in the question box (again = close).</summary>
    public void InvokeAsk()
    {
        InvokePage(Ask);
        Dispatcher.BeginInvoke(new Action(() => { if (_expanded && _pageIndex == Ask) { SetAskEngine(_askEngine); _askInput.Focus(); } }), DispatcherPriority.Input);
    }
}
