using System.Text.Json;

namespace TubeDrop.InnerTube.Auth;

/// <summary>
/// Everything needed to make authenticated InnerTube calls, captured at runtime
/// from the signed-in WebView2 page (§4). Never persisted to disk by TubeDrop —
/// the WebView2 profile is the only durable store.
/// </summary>
public sealed record InnerTubeSession
{
    /// <summary>Full cookie header value for *.youtube.com / *.google.com.</summary>
    public required string CookieHeader { get; init; }

    /// <summary>SAPISID (or __Secure-3PAPISID) cookie value used for SAPISIDHASH.</summary>
    public required string Sapisid { get; init; }

    /// <summary>INNERTUBE_API_KEY extracted from ytcfg of music.youtube.com.</summary>
    public required string MusicApiKey { get; init; }

    /// <summary>INNERTUBE_CONTEXT (raw JSON) from music.youtube.com — WEB_REMIX client.</summary>
    public required JsonElement MusicContext { get; init; }

    /// <summary>INNERTUBE_API_KEY from www.youtube.com; empty until first WEB call is needed.</summary>
    public string WebApiKey { get; init; } = "";

    /// <summary>INNERTUBE_CONTEXT (raw JSON) from www.youtube.com — WEB client; null until captured.</summary>
    public JsonElement? WebContext { get; init; }

    public string VisitorData { get; init; } = "";

    /// <summary>X-Goog-AuthUser index (multi-account sessions).</summary>
    public string AuthUser { get; init; } = "0";

    /// <summary>Signed-in account display name, best-effort from the login page (may be empty).</summary>
    public string AccountName { get; init; } = "";

    /// <summary>Signed-in account email/handle, best-effort (may be empty).</summary>
    public string AccountEmail { get; init; } = "";

    /// <summary>Account avatar URL, best-effort from the login page (may be empty → UI shows initials).</summary>
    public string AvatarUrl { get; init; } = "";
}

/// <summary>Provides the current session to InnerTube clients; raised when it changes.</summary>
public interface ISessionProvider
{
    InnerTubeSession? Current { get; }

    /// <summary>Raised when the session expires (401/403/consent) — UI shows a re-login banner (§4.6).</summary>
    event EventHandler? SessionExpired;

    /// <summary>Called by the transport when YouTube rejects our credentials.</summary>
    void NotifyExpired();
}
