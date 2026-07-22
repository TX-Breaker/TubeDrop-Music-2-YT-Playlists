using System.Text.RegularExpressions;
using TubeDrop.Core.Ingestion;

namespace TubeDrop.Core.Matching.Refiners;

/// <summary>
/// First rung of the fallback ladder (§8.1), always on, zero cost: strips
/// parentheses/brackets and <c>feat.</c> credits, swaps artist↔title, removes
/// leading track numbers, and adds AnyAscii transliterations — producing extra
/// query variants beyond the base set.
/// </summary>
public sealed partial class DeterministicRefiner : IQueryRefiner
{
    public string Name => "deterministic";

    [GeneratedRegex(@"[\(\[\{][^\)\]\}]*[\)\]\}]")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"\b(feat\.?|ft\.?|featuring)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturingTail();

    [GeneratedRegex(@"^\s*\d{1,3}\s*[-.\s]+")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    public Task<IReadOnlyList<string>> RefineAsync(TrackInfo track, CancellationToken ct = default)
    {
        var artist = Clean(track.Artist);
        var title = Clean(track.Title);
        var queries = new List<string>();

        void Add(string q)
        {
            var t = MultiSpace().Replace(q, " ").Trim();
            if (t.Length > 0 && !queries.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
            {
                queries.Add(t);
            }
        }

        if (artist.Length > 0)
        {
            Add($"{artist} {title}");
            Add($"{title} {artist}"); // swap
        }

        Add(title);

        // Transliterated forms for non-Latin scripts.
        if (TextNormalizer.HasNonLatin(artist) || TextNormalizer.HasNonLatin(title))
        {
            var at = TextNormalizer.Transliterated(artist);
            var tt = TextNormalizer.Transliterated(title);
            if (at.Length > 0)
            {
                Add($"{at} {tt}");
            }

            Add(tt);
        }

        return Task.FromResult<IReadOnlyList<string>>(queries);
    }

    /// <summary>Strips brackets, featuring credits, leading track numbers, and release noise.</summary>
    internal static string Clean(string value)
    {
        var cleaned = Brackets().Replace(value, " ");
        cleaned = FeaturingTail().Replace(cleaned, " ");
        cleaned = LeadingNumber().Replace(cleaned, " ");
        cleaned = FilenameHeuristics.CleanNoise(cleaned);
        return MultiSpace().Replace(cleaned, " ").Trim();
    }
}
