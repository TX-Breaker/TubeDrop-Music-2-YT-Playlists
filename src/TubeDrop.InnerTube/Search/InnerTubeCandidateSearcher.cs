using Microsoft.Extensions.Logging;
using TubeDrop.Core.Matching;

namespace TubeDrop.InnerTube.Search;

/// <summary>
/// Adapts <see cref="ISearchClient"/> to the Core matching contract, top N per
/// source (§7). YouTube Music is searched first; plain YouTube (WEB) is only a
/// fallback for the "All" scope when YTM returned nothing — YTM's canonical
/// song entries are more reliable. Resilient per source: a single failing source
/// is skipped and the others still contribute.
/// </summary>
public sealed class InnerTubeCandidateSearcher(
    ISearchClient searchClient, ILogger<InnerTubeCandidateSearcher> logger) : ICandidateSearcher
{
    private const int TopNPerSource = 10;

    public async Task<IReadOnlyList<MatchCandidate>> SearchAsync(
        string query, SearchScope scope, CancellationToken ct = default)
    {
        var results = new List<MatchCandidate>();
        var anyFailed = false;

        // --- YouTube Music first ---
        if (scope is SearchScope.YtmSongs or SearchScope.YtmSongsAndVideos or SearchScope.All)
        {
            anyFailed |= !await TryAddAsync(results, "YTM songs",
                () => searchClient.SearchYtmSongsAsync(query, ct), ct).ConfigureAwait(false);
        }

        if (scope is SearchScope.YtmSongsAndVideos or SearchScope.All)
        {
            anyFailed |= !await TryAddAsync(results, "YTM videos",
                () => searchClient.SearchYtmVideosAsync(query, ct), ct).ConfigureAwait(false);
        }

        // --- Plain YouTube: only the dedicated scope, or as an "All" fallback ---
        var wantYouTube = scope is SearchScope.YouTube
                          || (scope is SearchScope.All && results.Count == 0);
        if (wantYouTube)
        {
            anyFailed |= !await TryAddAsync(results, "YouTube",
                () => searchClient.SearchYouTubeAsync(query, ct), ct).ConfigureAwait(false);
        }

        // Every attempted source failed → surface it (Error, not a false "no match").
        if (anyFailed && results.Count == 0)
        {
            throw new InvalidOperationException("All search sources failed for this query.");
        }

        return results;
    }

    private async Task<bool> TryAddAsync(
        List<MatchCandidate> results, string source,
        Func<Task<IReadOnlyList<MatchCandidate>>> search, CancellationToken ct)
    {
        try
        {
            results.AddRange((await search().ConfigureAwait(false)).Take(TopNPerSource));
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Search source {Source} failed", source);
            return false;
        }
    }
}
