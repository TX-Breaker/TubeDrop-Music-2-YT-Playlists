using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace TubeDrop.App.Services;

/// <summary>
/// Checks the GitHub Releases feed for updates on startup (§13). Everything is
/// best-effort: a missing feed, no network, or a non-installed (dev) build all
/// degrade silently. When an update is found, <see cref="UpdateAvailable"/>
/// fires so the shell can show a toast.
/// </summary>
public sealed class UpdateService
{
    // Public releases feed; empty disables checks.
    private const string RepoUrl = "https://github.com/TX-Breaker/TubeDrop-Music-2-YT-Playlists";

    private readonly ILogger<UpdateService> _logger;
    private UpdateManager? _manager;
    private UpdateInfo? _pending;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<string>? UpdateAvailable;

    public async Task CheckAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoUrl) || RepoUrl.Contains("OWNER"))
        {
            _logger.LogDebug("Update feed not configured — skipping update check");
            return;
        }

        try
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));
            if (!_manager.IsInstalled)
            {
                _logger.LogDebug("Not an installed build — skipping update check");
                return;
            }

            _pending = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (_pending is not null)
            {
                var version = _pending.TargetFullRelease.Version.ToString();
                _logger.LogInformation("Update available: {Version}", version);
                UpdateAvailable?.Invoke(this, version);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed — continuing without updating");
        }
    }

    /// <summary>Downloads and applies the pending update, then relaunches.</summary>
    public async Task ApplyAsync()
    {
        if (_manager is null || _pending is null)
        {
            return;
        }

        try
        {
            await _manager.DownloadUpdatesAsync(_pending).ConfigureAwait(false);
            _manager.ApplyUpdatesAndRestart(_pending);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Applying update failed");
        }
    }
}
