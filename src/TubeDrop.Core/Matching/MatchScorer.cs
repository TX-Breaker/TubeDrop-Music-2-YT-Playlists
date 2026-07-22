using FuzzySharp;
using TubeDrop.Core.Ingestion;

namespace TubeDrop.Core.Matching;

/// <summary>A candidate with its computed score and penalty breakdown.</summary>
public sealed record ScoredCandidate(MatchCandidate Candidate, double Score, IReadOnlyList<string> PenaltyReasons);

/// <summary>
/// Scoring per §7, in [0,1]:
///   fuzzy title (token_set_ratio) ×0.40
/// + fuzzy artist ×0.30
/// + duration ×0.20 (1.0 at ≤3 s delta, linear to 0 at ≥15 s)
/// + authority ×0.10 (official/Topic/ATV/verified)
/// − capped variant penalties (live/cover/remix/… present only in the candidate).
/// </summary>
public static class MatchScorer
{
    private const double TitleWeight = 0.40;
    private const double ArtistWeight = 0.30;
    private const double DurationWeight = 0.20;
    private const double AuthorityWeight = 0.10;
    private const double PenaltyPerVariant = 0.25;
    private const double PenaltyCap = 0.50;

    /// <summary>Variant markers penalized when the candidate has them and the source does not.</summary>
    private static readonly string[] VariantMarkers =
    [
        "live", "cover", "remix", "sped up", "slowed", "reverb",
        "reaction", "8d", "karaoke", "instrumental",
    ];

    public static ScoredCandidate Score(TrackInfo source, MatchCandidate candidate)
    {
        var sourceTitle = TextNormalizer.ForComparison(source.Title);
        var candidateTitle = TextNormalizer.ForComparison(candidate.Title);

        var titleScore = Fuzz.TokenSetRatio(sourceTitle, candidateTitle) / 100.0;

        // Artist: compare against the candidate's artist list (or channel).
        // Unknown source artist → neutral 0.5, we can neither confirm nor deny.
        double artistScore;
        var sourceArtist = TextNormalizer.ForComparison(source.Artist);
        if (sourceArtist.Length == 0)
        {
            artistScore = 0.5;
        }
        else
        {
            var candidateArtists = candidate.Artists.Count > 0
                ? candidate.Artists
                : [candidate.Channel];
            artistScore = candidateArtists
                .Select(a => Fuzz.TokenSetRatio(sourceArtist, TextNormalizer.ForComparison(a)) / 100.0)
                .DefaultIfEmpty(0)
                .Max();
        }

        // Duration: unknown on either side → neutral 0.5.
        double durationScore;
        if (source.DurationSeconds <= 0 || candidate.DurationSeconds <= 0)
        {
            durationScore = 0.5;
        }
        else
        {
            var delta = Math.Abs(source.DurationSeconds - candidate.DurationSeconds);
            durationScore = delta <= 3 ? 1.0
                : delta >= 15 ? 0.0
                : 1.0 - (delta - 3) / 12.0;
        }

        var authorityScore = HasAuthority(candidate) ? 1.0 : 0.0;

        var score = titleScore * TitleWeight
                    + artistScore * ArtistWeight
                    + durationScore * DurationWeight
                    + authorityScore * AuthorityWeight;

        var penalties = new List<string>();
        var penalty = 0.0;
        var comparableSource = $"{sourceTitle} {TextNormalizer.ForComparison(source.Album)}";
        foreach (var marker in VariantMarkers)
        {
            if (ContainsMarker(candidateTitle, marker) && !ContainsMarker(comparableSource, marker))
            {
                penalty += PenaltyPerVariant;
                penalties.Add(marker);
            }
        }

        score -= Math.Min(penalty, PenaltyCap);

        return new ScoredCandidate(candidate, Math.Clamp(score, 0.0, 1.0), penalties);
    }

    public static bool HasAuthority(MatchCandidate candidate) =>
        candidate.IsOfficialArtistChannel
        || candidate.Channel.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase)
        || candidate.Badges.Any(b => b.Contains("official", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsMarker(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + marker.Length;
            var afterOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            index = text.IndexOf(marker, index + 1, StringComparison.Ordinal);
        }

        return false;
    }
}
