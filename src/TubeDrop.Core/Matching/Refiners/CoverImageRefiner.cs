using Microsoft.Extensions.Logging;
using TubeDrop.Core.Cover;
using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Settings;

namespace TubeDrop.Core.Matching.Refiners;

/// <summary>
/// Fallback-ladder rung that reverse-image-searches the track's embedded cover
/// (via the user's Google session, keyless) and turns the best-guess text into
/// extra YouTube queries. The Genre tag is appended as a light disambiguation
/// hint. Runs only when the base queries didn't already find a good match, so a
/// cover lookup happens at most once per hard track. Degrades to no queries
/// whenever disabled, unavailable, or the cover yields nothing.
/// </summary>
public sealed class CoverImageRefiner(
    ICoverImageLookup lookup,
    ISettingsStore settings,
    ILogger<CoverImageRefiner> logger) : IQueryRefiner
{
    public string Name => "cover";

    public async Task<IReadOnlyList<string>> RefineAsync(TrackInfo track, CancellationToken ct = default)
    {
        if (!settings.Current.CoverSearchEnabled || !lookup.IsAvailable || track.CoverArt is not { Length: > 0 })
        {
            return [];
        }

        try
        {
            var result = await lookup.LookupAsync(track.CoverArt, ct).ConfigureAwait(false);
            if (result.IsEmpty)
            {
                return [];
            }

            var genre = track.Genre.Trim();
            var queries = new List<string>();
            void Add(string? text)
            {
                var q = FilenameHeuristics.CleanNoise(text ?? "").Trim();
                if (q.Length >= 3 && !queries.Any(x => string.Equals(x, q, StringComparison.OrdinalIgnoreCase)))
                {
                    queries.Add(q);
                }
            }

            Add(result.BestGuess);
            if (genre.Length > 0)
            {
                Add($"{result.BestGuess} {genre}");
            }

            foreach (var suggestion in result.Suggestions)
            {
                Add(suggestion);
            }

            logger.LogInformation("Cover lookup for {Path} → {Count} query candidate(s)",
                track.SourcePath, queries.Count);
            return queries;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cover reverse-image lookup failed for {Path}", track.SourcePath);
            return [];
        }
    }
}
