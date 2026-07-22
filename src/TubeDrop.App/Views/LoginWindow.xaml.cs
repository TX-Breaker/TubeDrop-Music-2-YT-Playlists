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

    // Google account chooser → lets the user switch to (or add) another account,
    // then continues back to YouTube Music where the new session is captured.
    private const string AccountChooserUrl =
        "https://accounts.google.com/AccountChooser?continue=https%3A%2F%2Fmusic.youtube.com%2F";

    private const string YtcfgScript =
        "JSON.stringify({" +
        "apiKey: (window.ytcfg && ytcfg.get('INNERTUBE_API_KEY')) || null," +
        "context: (window.ytcfg && ytcfg.get('INNERTUBE_CONTEXT')) || null," +
        "visitorData: (window.ytcfg && ytcfg.get('VISITOR_DATA')) || null," +
        "sessionIndex: (window.ytcfg && ytcfg.get('SESSION_INDEX')) || null})";

    // Best-effort account name + avatar scraped from the signed-in page DOM.
    private const string AccountScript =
        "(function(){try{" +
        "var img=document.querySelector('ytmusic-settings-button img')||" +
        "[].slice.call(document.images).filter(function(i){return /googleusercontent|ggpht/.test(i.src||'')})[0];" +
        "var b=document.querySelector('ytmusic-settings-button');" +
        "var name=(b&&(b.getAttribute('aria-label')||b.title))||'';" +
        "return JSON.stringify({avatar:img?img.src:'',name:name});" +
        "}catch(e){return JSON.stringify({avatar:'',name:''});}})()";

    private bool _revealed;
    private bool _completed;

    /// <summary>When true, opens the Google account chooser so the user can switch account.</summary>
    public bool SwitchAccount { get; init; }

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

            if (SwitchAccount)
            {
                // Show the window so the user can pick another account.
                Reveal();
                WebView.CoreWebView2.Navigate(AccountChooserUrl);
            }
            else
            {
                WebView.CoreWebView2.Navigate(MusicOrigin + "/");
            }
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

        var (accountName, avatarUrl) = await TryReadAccountAsync();

        Session = new InnerTubeSession
        {
            CookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")),
            Sapisid = sapisid,
            MusicApiKey = ytcfg.ApiKey,
            MusicContext = ytcfg.Context,
            VisitorData = ytcfg.VisitorData,
            AuthUser = authUser,
            AccountName = accountName,
            AvatarUrl = avatarUrl,
        };

        _completed = true;
        DialogResult = true;
    }

    /// <summary>Best-effort name + avatar from the page DOM; empty on any failure.</summary>
    private async Task<(string Name, string Avatar)> TryReadAccountAsync()
    {
        try
        {
            var result = await WebView.CoreWebView2.ExecuteScriptAsync(AccountScript);
            var json = JsonSerializer.Deserialize<string>(result);
            if (string.IsNullOrEmpty(json))
            {
                return ("", "");
            }

            using var doc = JsonDocument.Parse(json);
            var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var avatar = doc.RootElement.TryGetProperty("avatar", out var a) ? a.GetString() ?? "" : "";
            return (name, avatar);
        }
        catch (Exception)
        {
            return ("", "");
        }
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
