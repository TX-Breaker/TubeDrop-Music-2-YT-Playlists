using Microsoft.Extensions.Logging;
using TubeDrop.Core.Matching;

namespace TubeDrop.InnerTube.Search;

/// <summary>
/// Adapts <see cref="ISearchClient"/> to the Core matching contract, top N per
/// source (§7). Resilient per source: if one source fails (e.g. a transient
/// error), it is skipped and the others still contribute — one flaky source
/// never fails the whole track.
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

        if (scope is SearchScope.YouTube or SearchScope.All)
        {
            anyFailed |= !await TryAddAsync(results, "YouTube",
                () => searchClient.SearchYouTubeAsync(query, ct), ct).ConfigureAwait(false);
        }

        // If every attempted source failed, surface it so the track is Error, not
        // a false "no match". If at least one worked, degrade quietly.
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
