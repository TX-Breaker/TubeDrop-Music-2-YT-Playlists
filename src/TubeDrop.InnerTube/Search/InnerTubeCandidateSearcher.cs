using TubeDrop.Core.Matching;

namespace TubeDrop.InnerTube.Search;

/// <summary>Adapts <see cref="ISearchClient"/> to the Core matching contract, top N per source (§7).</summary>
public sealed class InnerTubeCandidateSearcher(ISearchClient searchClient) : ICandidateSearcher
{
    private const int TopNPerSource = 10;

    public async Task<IReadOnlyList<MatchCandidate>> SearchAsync(
        string query, SearchScope scope, CancellationToken ct = default)
    {
        var results = new List<MatchCandidate>();

        if (scope is SearchScope.YtmSongs or SearchScope.YtmSongsAndVideos or SearchScope.All)
        {
            results.AddRange((await searchClient.SearchYtmSongsAsync(query, ct).ConfigureAwait(false))
                .Take(TopNPerSource));
        }

        if (scope is SearchScope.YtmSongsAndVideos or SearchScope.All)
        {
            results.AddRange((await searchClient.SearchYtmVideosAsync(query, ct).ConfigureAwait(false))
                .Take(TopNPerSource));
        }

        if (scope is SearchScope.YouTube or SearchScope.All)
        {
            results.AddRange((await searchClient.SearchYouTubeAsync(query, ct).ConfigureAwait(false))
                .Take(TopNPerSource));
        }

        return results;
    }
}
