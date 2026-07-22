using TubeDrop.Core.Ingestion;

namespace TubeDrop.Core.Matching;

/// <summary>
/// Builds the ordered list of search query candidates for a track (§7):
/// "{artist} {title}", "{title} {artist}", title-only, plus AnyAscii
/// transliterated variants for non-Latin metadata (§8.1). Deduplicated,
/// noise-stripped, order preserved.
/// </summary>
public static class QueryBuilder
{
    public static IReadOnlyList<string> Build(TrackInfo track)
    {
        var artist = FilenameHeuristics.CleanNoise(track.Artist);
        var title = FilenameHeuristics.CleanNoise(track.Title);

        var queries = new List<string>();
        void Add(string query)
        {
            var trimmed = query.Trim();
            if (trimmed.Length > 0 &&
                !queries.Any(q => string.Equals(q, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                queries.Add(trimmed);
            }
        }

        if (artist.Length > 0)
        {
            Add($"{artist} {title}");
            Add($"{title} {artist}");
        }

        Add(title);

        // Transliterated variants only when they differ (non-Latin scripts).
        if (TextNormalizer.HasNonLatin(artist) || TextNormalizer.HasNonLatin(title))
        {
            var artistTr = TextNormalizer.Transliterated(artist);
            var titleTr = TextNormalizer.Transliterated(title);
            if (artistTr.Length > 0)
            {
                Add($"{artistTr} {titleTr}");
            }

            Add(titleTr);
        }

        return queries;
    }
}
