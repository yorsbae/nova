using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Nova.Core.Ask;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Gets a Google AI Mode answer without an API key: a hidden, off-screen browser opens the AI Mode page for the question,
/// waits until the text stops changing, and returns only the cleaned answer text. No login is used and nothing is kept
/// except the browser's own cookie folder in %LocalAppData%\NOVA\webview. It reads a web page, so it can break when Google changes the page,
/// and Google may ask for a verification — then the caller is told to open the question in the real browser.
/// </summary>
public sealed class AiModeClient : IDisposable
{
    const string Script = "(function(){var m=document.querySelector('[role=\"main\"]')||document.querySelector('main')||document.body;" +
                          "return JSON.stringify({url:location.href,text:(m.innerText||'')});})()";

    Window? _host;
    WebView2? _web;
    readonly SemaphoreSlim _one = new(1, 1);

    async Task EnsureAsync()
    {
        if (_web is not null) return;
        _web = new WebView2();
        _host = new Window
        {
            Width = 900, Height = 700, Left = -32000, Top = -32000, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            ShowActivated = false, Content = _web, Title = "NOVA AI Mode (hidden)"
        };
        _host.SourceInitialized += (_, _) => NativeMethods.SetOverlayStyle(_host, noActivate: true);
        _host.Show();
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVA", "webview");
        var env = await CoreWebView2Environment.CreateAsync(null, folder);
        await _web.EnsureCoreWebView2Async(env);
        _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;   // never open extra windows
        _web.CoreWebView2.IsMuted = true;
    }

    /// <summary>The style is added after the question text, so Google writes the answer that way (not guaranteed). Returns (true, answer) or (false, short reason). One question at a time; each question starts a fresh AI Mode page (no memory of earlier questions).</summary>
    public async Task<(bool Ok, string Text)> AskAsync(string query, string style, int maxChars, CancellationToken ct)
    {
        await _one.WaitAsync(ct);
        try
        {
            try { await EnsureAsync(); }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                FileLog.Write($"WebView2 unavailable: {ex.GetType().Name}");
                return (false, "WebView2 tidak ada. Pasang Microsoft Edge WebView2 Runtime, atau pakai mesin lain.");
            }

            var asked = AskSearch.ApplyStyle(query, style);
            var loaded = new TaskCompletionSource();
            void Done(object? s, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult();
            _web!.CoreWebView2.NavigationCompleted += Done;
            try
            {
                _web.CoreWebView2.Navigate(AskSearch.BuildUrl(AskEngine.GoogleAi, asked));
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            }
            finally { _web.CoreWebView2.NavigationCompleted -= Done; }

            string last = ""; int stable = 0;
            for (int i = 0; i < 60 && !ct.IsCancellationRequested; i++)   // up to ~42 s
            {
                await Task.Delay(700, ct);
                var (url, raw) = await ReadAsync();
                if (AskSearch.LooksBlocked(url, raw))
                    return (false, "Google meminta verifikasi. Pakai mesin \"AI Mode\" untuk membukanya di browser.");
                var answer = AskSearch.CleanAnswer(raw, asked, maxChars);
                if (answer.Length > 40 && answer == last) { if (++stable >= 3) return (true, answer); }
                else { stable = 0; last = answer; }
            }
            ct.ThrowIfCancellationRequested();
            return last.Length > 40 ? (true, last) : (false, "Jawaban AI Mode tidak ketemu. Coba mesin \"AI Mode\" (browser).");
        }
        catch (OperationCanceledException) { return (false, "Dibatalkan."); }
        catch (TimeoutException) { return (false, "Halaman terlalu lama dimuat. Cek internet."); }
        finally { _one.Release(); }
    }

    async Task<(string Url, string Text)> ReadAsync()
    {
        try
        {
            var json = await _web!.CoreWebView2.ExecuteScriptAsync(Script);   // a JSON string holding a JSON string
            var inner = JsonSerializer.Deserialize<string>(json) ?? "";
            using var doc = JsonDocument.Parse(inner);
            return (doc.RootElement.GetProperty("url").GetString() ?? "", doc.RootElement.GetProperty("text").GetString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { return ("", ""); }
    }

    public void Dispose() { _web?.Dispose(); _host?.Close(); _web = null; _host = null; }
}
