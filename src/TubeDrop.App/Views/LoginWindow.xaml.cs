using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TubeDrop.App.Services;
using TubeDrop.InnerTube.Auth;

namespace TubeDrop.App.Views;

/// <summary>
/// Embedded Google sign-in (§4). Opens invisible: when the persisted profile is
/// still signed in, the session is extracted and the window closes before the
/// user ever sees it; otherwise it becomes visible for interactive login.
/// </summary>
public partial class LoginWindow : Wpf.Ui.Controls.FluentWindow
{
    private const string MusicOrigin = "https://music.youtube.com";

    private const string YtcfgScript =
        "JSON.stringify({" +
        "apiKey: (window.ytcfg && ytcfg.get('INNERTUBE_API_KEY')) || null," +
        "context: (window.ytcfg && ytcfg.get('INNERTUBE_CONTEXT')) || null," +
        "visitorData: (window.ytcfg && ytcfg.get('VISITOR_DATA')) || null," +
        "sessionIndex: (window.ytcfg && ytcfg.get('SESSION_INDEX')) || null})";

    private bool _revealed;
    private bool _completed;

    public InnerTubeSession? Session { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        Opacity = 0; // stay invisible until we know interactive login is needed
    }

    private async void LoginWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: AuthService.ProfileDirectory);
            await WebView.EnsureCoreWebView2Async(environment);

            WebView.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                // Keep Google's popup flows inside this window.
                args.Handled = true;
                WebView.CoreWebView2.Navigate(args.Uri);
            };
            WebView.CoreWebView2.NavigationCompleted += CoreWebView2_OnNavigationCompleted;
            WebView.CoreWebView2.Navigate(MusicOrigin + "/");
        }
        catch (Exception)
        {
            // WebView2 runtime missing/broken — surface the window with the error page.
            Reveal();
        }
    }

    private async void CoreWebView2_OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_completed)
        {
            return;
        }

        var uri = new Uri(WebView.CoreWebView2.Source);
        if (!uri.Host.EndsWith("music.youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            // Google sign-in / consent pages → the user has to interact.
            Reveal();
            return;
        }

        var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync(MusicOrigin);
        var sapisid = cookies.FirstOrDefault(c => c.Name == "SAPISID")?.Value
                      ?? cookies.FirstOrDefault(c => c.Name == "__Secure-3PAPISID")?.Value;
        if (string.IsNullOrEmpty(sapisid))
        {
            Reveal();
            return;
        }

        var scriptResult = await WebView.CoreWebView2.ExecuteScriptAsync(YtcfgScript);
        // ExecuteScriptAsync returns the JSON-encoded JS value; ours is a string,
        // so unwrap one level to get the raw JSON blob.
        var json = JsonSerializer.Deserialize<string>(scriptResult);
        var ytcfg = YtcfgParser.Parse(json);
        if (ytcfg is null)
        {
            // Signed in but page config not ready (e.g. consent interstitial) — wait
            // for the next navigation; reveal so the user can complete any prompt.
            Reveal();
            return;
        }

        var authUser = "0";
        try
        {
            using var doc = JsonDocument.Parse(json!);
            if (doc.RootElement.TryGetProperty("sessionIndex", out var idx) &&
                idx.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                authUser = idx.ToString();
            }
        }
        catch (JsonException)
        {
        }

        Session = new InnerTubeSession
        {
            CookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")),
            Sapisid = sapisid,
            MusicApiKey = ytcfg.ApiKey,
            MusicContext = ytcfg.Context,
            VisitorData = ytcfg.VisitorData,
            AuthUser = authUser,
        };

        _completed = true;
        DialogResult = true;
    }

    private void Reveal()
    {
        if (_revealed)
        {
            return;
        }

        _revealed = true;
        Opacity = 1;
        ShowInTaskbar = true;
        Activate();
    }
}
