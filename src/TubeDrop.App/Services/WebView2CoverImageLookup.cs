using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TubeDrop.Core.Cover;

namespace TubeDrop.App.Services;

/// <summary>
/// Keyless cover reverse-image lookup using the user's own signed-in Google
/// session inside a hidden WebView2 (no API key / cloud console — by design,
/// same approach as the InnerTube layer). Drives Google Lens: uploads the cover
/// via the Chrome DevTools Protocol (a file input can't be set from plain JS),
/// waits for results, and scrapes best-guess text.
///
/// EXPERIMENTAL and best-effort: Google's markup changes and is anti-bot, so any
/// failure returns <see cref="CoverLookupResult.Empty"/>. When capture is on
/// (TUBEDROP_CAPTURE=1) the results page text is dumped so the selectors can be
/// refined against real output.
/// </summary>
public sealed class WebView2CoverImageLookup : ICoverImageLookup
{
    private const string LensUrl = "https://lens.google.com/";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);

    private readonly ILogger<WebView2CoverImageLookup> _logger;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private Window? _host;
    private WebView2? _webView;

    public WebView2CoverImageLookup(ILogger<WebView2CoverImageLookup> logger)
    {
        _logger = logger;
    }

    public bool IsAvailable => true; // WebView2 runtime ships with the app deps

    public async Task<CoverLookupResult> LookupAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        await _oneAtATime.WaitAsync(ct).ConfigureAwait(false);
        var tempFile = Path.Combine(Path.GetTempPath(), $"tubedrop_cover_{Guid.NewGuid():N}.jpg");
        try
        {
            await File.WriteAllBytesAsync(tempFile, imageBytes, ct).ConfigureAwait(false);

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return CoverLookupResult.Empty;
            }

            return await dispatcher.InvokeAsync(
                async () => await RunLookupAsync(tempFile, ct).ConfigureAwait(true),
                DispatcherPriority.Background, ct).Task.Unwrap().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cover lookup failed");
            return CoverLookupResult.Empty;
        }
        finally
        {
            try { File.Delete(tempFile); } catch (IOException) { }
            _oneAtATime.Release();
        }
    }

    private async Task<CoverLookupResult> RunLookupAsync(string imagePath, CancellationToken ct)
    {
        await EnsureWebViewAsync().ConfigureAwait(true);
        var core = _webView!.CoreWebView2;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);
        var token = timeoutCts.Token;

        var loaded = new TaskCompletionSource();
        void OnNav(object? _, CoreWebView2NavigationCompletedEventArgs __) => loaded.TrySetResult();
        core.NavigationCompleted += OnNav;
        try
        {
            core.Navigate(LensUrl);
            await WaitAsync(loaded.Task, token).ConfigureAwait(true);

            // The Lens upload control exposes a file input once the page settles.
            var objectId = await FindFileInputAsync(core, token).ConfigureAwait(true);
            if (objectId is null)
            {
                return CoverLookupResult.Empty;
            }

            // Set the file via CDP (JS can't assign a File to an <input type=file>).
            await core.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles",
                JsonSerializer.Serialize(new { files = new[] { imagePath }, objectId })).ConfigureAwait(true);

            // Uploading navigates to the results; wait then scrape.
            await Task.Delay(3500, token).ConfigureAwait(true);
            return await ScrapeAsync(core).ConfigureAwait(true);
        }
        finally
        {
            core.NavigationCompleted -= OnNav;
        }
    }

    /// <summary>Runtime.evaluate to locate a file input, returning its CDP objectId.</summary>
    private static async Task<string?> FindFileInputAsync(CoreWebView2 core, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var json = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                JsonSerializer.Serialize(new
                {
                    expression = "document.querySelector('input[type=file]')",
                    returnByValue = false,
                })).ConfigureAwait(true);

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("objectId", out var objectId) &&
                    objectId.ValueKind == JsonValueKind.String)
                {
                    return objectId.GetString();
                }
            }
            catch (JsonException)
            {
            }

            await Task.Delay(600, ct).ConfigureAwait(true);
        }

        return null;
    }

    private async Task<CoverLookupResult> ScrapeAsync(CoreWebView2 core)
    {
        // Best-guess: the page title minus the Google/Lens suffix; suggestions:
        // the text of related-search chips / result links near the top.
        const string script =
            "(function(){try{" +
            "var title=(document.title||'').replace(/\\s*[-–|]\\s*Google.*$/i,'').trim();" +
            "var texts=[];" +
            "document.querySelectorAll('a,div[role=listitem],span').forEach(function(e){" +
            "var t=(e.innerText||'').trim();" +
            "if(t.length>=4&&t.length<=80&&texts.indexOf(t)<0)texts.push(t);});" +
            "return JSON.stringify({title:title,texts:texts.slice(0,12)});" +
            "}catch(e){return JSON.stringify({title:'',texts:[]});}})()";

        var raw = await core.ExecuteScriptAsync(script).ConfigureAwait(true);
        var json = JsonSerializer.Deserialize<string>(raw);
        if (string.IsNullOrEmpty(json))
        {
            return CoverLookupResult.Empty;
        }

        Capture(core.Source, json);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var suggestions = new List<string>();
            if (root.TryGetProperty("texts", out var texts) && texts.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in texts.EnumerateArray())
                {
                    if (el.GetString() is { Length: > 0 } s)
                    {
                        suggestions.Add(s);
                    }
                }
            }

            return new CoverLookupResult(title, suggestions);
        }
        catch (JsonException)
        {
            return CoverLookupResult.Empty;
        }
    }

    private async Task EnsureWebViewAsync()
    {
        if (_webView is not null)
        {
            return;
        }

        _host = new Window
        {
            Width = 900,
            Height = 700,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -10000,
            Top = -10000,
            Opacity = 0,
        };
        _webView = new WebView2();
        _host.Content = _webView;
        _host.Show(); // required for the WebView2 to have a visual tree; kept off-screen

        var environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: AuthService.ProfileDirectory).ConfigureAwait(true);
        await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
    }

    private static async Task WaitAsync(Task task, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        await using (ct.Register(() => tcs.TrySetResult()))
        {
            await Task.WhenAny(task, tcs.Task).ConfigureAwait(true);
        }

        ct.ThrowIfCancellationRequested();
    }

    private void Capture(string sourceUrl, string scrapedJson)
    {
        if (Environment.GetEnvironmentVariable("TUBEDROP_CAPTURE") is not ("1" or "true"))
        {
            return;
        }

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TubeDrop", "captures");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, $"cover_lens_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}.json"),
                $"{{\"source\":{JsonSerializer.Serialize(sourceUrl)},\"scraped\":{scrapedJson}}}",
                Encoding.UTF8);
        }
        catch (Exception)
        {
        }
    }
}
