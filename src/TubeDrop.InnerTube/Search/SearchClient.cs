using TubeDrop.Core.Matching;
using TubeDrop.InnerTube.Http;

namespace TubeDrop.InnerTube.Search;

public interface ISearchClient
{
    Task<IReadOnlyList<MatchCandidate>> SearchYtmSongsAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<MatchCandidate>> SearchYtmVideosAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<MatchCandidate>> SearchYouTubeAsync(string query, CancellationToken ct = default);
}

/// <summary>youtubei/v1/search for the three scopes of §7.</summary>
public sealed class SearchClient(InnerTubeTransport transport) : ISearchClient
{
    // Filter params verified against captured traffic (tests/fixtures, 2026-07-22):
    // with these, the response's musicShelfRenderer contains only Songs / Videos.
    internal const string SongsParams = "EgWKAQIIAWoMEA4QChADEAQQCRAF";
    internal const string VideosParams = "EgWKAQIQAWoMEA4QChADEAQQCRAF";

    public async Task<IReadOnlyList<MatchCandidate>> SearchYtmSongsAsync(string query, CancellationToken ct = default)
    {
        var root = await transport.PostAsync(InnerTubeTransport.MusicOrigin, "search",
            new Dictionary<string, object?> { ["query"] = query, ["params"] = SongsParams }, ct)
            .ConfigureAwait(false);
        return SearchResponseParser.ParseYtmSearch(root, CandidateSource.YtmSong);
    }

    public async Task<IReadOnlyList<MatchCandidate>> SearchYtmVideosAsync(string query, CancellationToken ct = default)
    {
        var root = await transport.PostAsync(InnerTubeTransport.MusicOrigin, "search",
            new Dictionary<string, object?> { ["query"] = query, ["params"] = VideosParams }, ct)
            .ConfigureAwait(false);
        return SearchResponseParser.ParseYtmSearch(root, CandidateSource.YtmVideo);
    }

    public async Task<IReadOnlyList<MatchCandidate>> SearchYouTubeAsync(string query, CancellationToken ct = default)
    {
        var root = await transport.PostAsync(InnerTubeTransport.WebOrigin, "search",
            new Dictionary<string, object?> { ["query"] = query }, ct)
            .ConfigureAwait(false);
        return SearchResponseParser.ParseYouTubeSearch(root);
    }
}
