using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nova.App.Integrations.Windows;
using Nova.Core.Clipboard;

namespace Nova.App.Features.ClipboardPanel;

/// <summary>Opt-in clipboard history list. Opened only on explicit request, so it may take keyboard focus.</summary>
public sealed class ClipboardWindow : Window
{
    readonly ClipboardHistory _history;
    readonly Action<string, bool> _notify;
    readonly StackPanel _list = new();
    readonly TextBlock _empty = new() { Text = "Nothing copied yet this session. Copy some text, a picture or a file and it shows up here.", Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap };

    public ClipboardWindow(ClipboardHistory history, Action<string, bool> notify)
    {
        _history = history; _notify = notify;
        Title = "NOVA Clipboard history";
        AutomationProperties.SetName(this, "NOVA clipboard history");
        Width = 420; Height = 460; MinWidth = 300; MinHeight = 240;
        Topmost = true; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "NovaWindow");

        var header = new TextBlock
        {
            Text = "Clipboard history. Text, pictures and copied files, kept in memory for this session only. Choose an item to copy it again.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");

        var clear = new Button { Content = "Clear history", Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(clear, "Clear all clipboard history");
        clear.Click += (_, _) => { _history.Clear(); _notify("Clipboard history cleared.", false); };
        var close = new Button { Content = "Close", IsCancel = true };
        AutomationProperties.SetName(close, "Close clipboard history");
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(clear); buttons.Children.Add(close);

        var dock = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(header, Dock.Top); DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(header); dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = _list });
        Content = dock;

        _history.Changed += Rebuild;
        Closed += (_, _) => _history.Changed -= Rebuild;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => { Activate(); (_list.Children.OfType<DockPanel>().SelectMany(d => d.Children.OfType<Button>()).FirstOrDefault() ?? buttons.Children.OfType<Button>().First()).Focus(); };
        Rebuild();
    }

    void Rebuild()
    {
        _list.Children.Clear();
        if (_history.Entries.Count == 0) { _list.Children.Add(_empty); return; }
        foreach (var entry in _history.Entries.ToList())
        {
            var label = new TextBlock { Text = ClipboardHistory.Describe(entry, 90), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "NovaOnSurface");
            object content = label;
            if (entry.Kind == ClipKind.Image && ClipboardTools.Decode(entry.Png, 160) is { } thumb)
            {
                var pic = new Image { Source = thumb, Height = 44, MaxWidth = 90, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) };
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(pic); line.Children.Add(label);
                content = line;
            }

            var copy = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(copy, "Copy again: " + ClipboardHistory.Describe(entry, 60));
            copy.Click += (_, _) =>
            {
                if (ClipboardTools.Restore(entry)) Close();
                else _notify("Couldn't copy that item; the clipboard is busy or the file is gone.", true);
            };
            var remove = new Button { Content = "\u2715", Width = 30, Margin = new Thickness(6, 0, 0, 0) };
            AutomationProperties.SetName(remove, "Remove from history: " + ClipboardHistory.Describe(entry, 40));
            remove.Click += (_, _) => _history.Remove(entry);

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(copy);
            _list.Children.Add(row);
        }
    }
}
