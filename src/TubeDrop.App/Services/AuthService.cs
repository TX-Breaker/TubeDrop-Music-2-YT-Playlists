using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using TubeDrop.App.Views;
using TubeDrop.InnerTube.Auth;

namespace TubeDrop.App.Services;

/// <summary>
/// Owns the InnerTube session for the app lifetime. The WebView2 profile under
/// %LOCALAPPDATA%\TubeDrop\profile is the durable login store (§4.2); this
/// service only keeps the extracted session in memory.
/// </summary>
public sealed class AuthService : ISessionProvider
{
    private readonly ILogger<AuthService> _logger;

    public AuthService(ILogger<AuthService> logger)
    {
        _logger = logger;
    }

    public static string ProfileDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubeDrop", "profile");

    public InnerTubeSession? Current { get; private set; }

    public event EventHandler? SessionExpired;
    public event EventHandler? SessionChanged;

    public bool IsSignedIn => Current is not null;

    public string AccountName => Current?.AccountName ?? "";
    public string AvatarUrl => Current?.AvatarUrl ?? "";

    /// <summary>
    /// Opens the login window. If the persisted WebView2 profile still holds a
    /// valid Google session the window closes itself without ever becoming
    /// visible; otherwise the user signs in interactively.
    /// </summary>
    public bool SignIn(Window? owner) => ShowLogin(owner, switchAccount: false);

    /// <summary>Opens the Google account chooser so the user can switch to another account (§4).</summary>
    public bool SwitchAccount(Window? owner) => ShowLogin(owner, switchAccount: true);

    private bool ShowLogin(Window? owner, bool switchAccount)
    {
        var window = new LoginWindow { SwitchAccount = switchAccount };
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }

        var result = window.ShowDialog();
        if (result == true && window.Session is not null)
        {
            Current = window.Session;
            _logger.LogInformation("Signed in (authUser={AuthUser}, name='{Name}')",
                Current.AuthUser, Current.AccountName);
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        _logger.LogInformation("Sign-in window closed without a session");
        return false;
    }

    public void SignOut()
    {
        Current = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyExpired()
    {
        _logger.LogWarning("InnerTube session rejected by YouTube — marking expired");
        Current = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
        SessionExpired?.Invoke(this, EventArgs.Empty);
    }
}
