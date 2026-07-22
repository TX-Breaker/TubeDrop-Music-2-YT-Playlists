using TubeDrop.Core.Ingestion;

namespace TubeDrop.Core.Matching;

public enum SearchScope
{
    YtmSongs,
    YtmSongsAndVideos,
    YouTube,
    All,
}

/// <summary>Search abstraction implemented by the InnerTube layer (top N per source, normalized).</summary>
public interface ICandidateSearcher
{
    Task<IReadOnlyList<MatchCandidate>> SearchAsync(string query, SearchScope scope, CancellationToken ct = default);
}

/// <summary>Fallback-ladder step contract (§8) — implementations arrive with M8.</summary>
public interface IQueryRefiner
{
    string Name { get; }

    /// <summary>Additional queries to try when the base queries found nothing good.</summary>
    Task<IReadOnlyList<string>> RefineAsync(TrackInfo track, CancellationToken ct = default);
}

public enum MatchStatus
{
    AutoMatched,
    FallbackMatched,

    /// <summary>Best-effort add in aggressive mode — flagged in the report (§7).</summary>
    AggressiveMatched,
    Unmatched,
}

public sealed record TrackMatchResult(
    TrackInfo Track,
    MatchStatus Status,
    ScoredCandidate? Best,
    string? UsedQuery,
    string? UsedRefiner);

public sealed record MatchingOptions
{
    public double Threshold { get; init; } = 0.75;
    public SearchScope Scope { get; init; } = SearchScope.YtmSongs;
    public bool AggressiveMode { get; init; }
}

/// <summary>
/// Fully automatic matcher (§7): base queries first, then the refiner ladder
/// in order (§8), stop on first success; below threshold → Unmatched (or
/// flagged best-effort in aggressive mode). Never silently guess-adds.
/// </summary>
public sealed class MatchingEngine(
    ICandidateSearcher searcher,
    IEnumerable<IQueryRefiner> refinerLadder)
{
    private readonly IReadOnlyList<IQueryRefiner> _refiners = refinerLadder.ToList();

    public async Task<TrackMatchResult> MatchAsync(
        TrackInfo track,
        MatchingOptions options,
        CancellationToken ct = default)
    {
        // Note: a missing artist is NOT a reason to skip up front. The whole
        // recognition chain (tags → filename → audio fingerprint) runs before we
        // get here; the track still searches (title-based), and skipping it is the
        // last resort — it only ends up Unmatched if nothing clears the threshold.
        ScoredCandidate? bestOverall = null;
        string? bestQuery = null;
        string? bestRefiner = null;

        // 1) Base queries (§7).
        var (best, query) = await TryQueriesAsync(QueryBuilder.Build(track), track, options, ct)
            .ConfigureAwait(false);
        if (best is not null && best.Score >= options.Threshold)
        {
            return new TrackMatchResult(track, MatchStatus.AutoMatched, best, query, null);
        }

        Keep(best, query, null);

        // 2) Fallback ladder (§8), ordered, stop on success.
        foreach (var refiner in _refiners)
        {
            ct.ThrowIfCancellationRequested();
            var refined = await refiner.RefineAsync(track, ct).ConfigureAwait(false);
            if (refined.Count == 0)
            {
                continue;
            }

            var (refinedBest, refinedQuery) = await TryQueriesAsync(refined, track, options, ct)
                .ConfigureAwait(false);
            if (refinedBest is not null && refinedBest.Score >= options.Threshold)
            {
                return new TrackMatchResult(track, MatchStatus.FallbackMatched, refinedBest, refinedQuery, refiner.Name);
            }

            Keep(refinedBest, refinedQuery, refiner.Name);
        }

        // 3) Below threshold everywhere.
        if (options.AggressiveMode && bestOverall is not null)
        {
            return new TrackMatchResult(track, MatchStatus.AggressiveMatched, bestOverall, bestQuery, bestRefiner);
        }

        return new TrackMatchResult(track, MatchStatus.Unmatched, bestOverall, bestQuery, bestRefiner);

        void Keep(ScoredCandidate? candidate, string? usedQuery, string? usedRefiner)
        {
            if (candidate is not null && (bestOverall is null || candidate.Score > bestOverall.Score))
            {
                bestOverall = candidate;
                bestQuery = usedQuery;
                bestRefiner = usedRefiner;
            }
        }
    }

    private async Task<(ScoredCandidate? Best, string? Query)> TryQueriesAsync(
        IReadOnlyList<string> queries,
        TrackInfo track,
        MatchingOptions options,
        CancellationToken ct)
    {
        ScoredCandidate? best = null;
        string? bestQuery = null;

        foreach (var query in queries)
        {
            ct.ThrowIfCancellationRequested();
            var candidates = await searcher.SearchAsync(query, options.Scope, ct).ConfigureAwait(false);
            foreach (var candidate in candidates)
            {
                var scored = MatchScorer.Score(track, candidate);
                if (best is null || scored.Score > best.Score)
                {
                    best = scored;
                    bestQuery = query;
                }
            }

            // Good enough — no need to burn more queries for this track.
            if (best is not null && best.Score >= options.Threshold)
            {
                return (best, bestQuery);
            }
        }

        return (best, bestQuery);
    }
}
