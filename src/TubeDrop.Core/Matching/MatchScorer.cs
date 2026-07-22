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

    /// <summary>Markers penalized only when the CANDIDATE has them and the source does not.</summary>
    private static readonly string[] VariantMarkers =
    [
        "live", "cover", "sped up", "slowed", "reverb",
        "reaction", "8d", "karaoke", "instrumental",
    ];

    /// <summary>
    /// Version-defining markers penalized when EITHER side has them and the other
    /// does not — a remix file must not match the original, and vice versa.
    /// </summary>
    private static readonly string[] VersionMarkers =
    [
        "remix", "vip", "bootleg", "flip", "mashup", "rework", "remake", "edit",
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
            var artistVsArtists = candidateArtists
                .Select(a => Fuzz.TokenSetRatio(sourceArtist, TextNormalizer.ForComparison(a)) / 100.0)
                .DefaultIfEmpty(0)
                .Max();

            // Remixes/edits: the source "artist" is often the remixer, whose name
            // sits in the candidate title while YouTube lists the original artist.
            // Credit the artist when its name tokens appear in the candidate title.
            var artistInTitle = ArtistTokensInTitle(sourceArtist, candidateTitle);

            artistScore = Math.Max(artistVsArtists, artistInTitle);
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

        // A near-exact title whose duration also matches is almost certainly the
        // right upload, even when the artist field differs (common for remixes
        // and edits). Don't let the artist veto it — floor the artist signal.
        if (titleScore >= 0.95 && durationScore >= 0.8)
        {
            artistScore = Math.Max(artistScore, 0.5);
        }

        var score = titleScore * TitleWeight
                    + artistScore * ArtistWeight
                    + durationScore * DurationWeight
                    + authorityScore * AuthorityWeight;

        var penalties = new List<string>();
        var penalty = 0.0;
        var comparableSource = NormalizeRemix($"{sourceTitle} {TextNormalizer.ForComparison(source.Album)}");
        var comparableCandidate = NormalizeRemix(candidateTitle);
        foreach (var marker in VariantMarkers)
        {
            if (ContainsMarker(comparableCandidate, marker) && !ContainsMarker(comparableSource, marker))
            {
                penalty += PenaltyPerVariant;
                penalties.Add(marker);
            }
        }

        // Version markers must match on BOTH sides: a remix file matching the
        // original (or the reverse) is the wrong recording.
        foreach (var marker in VersionMarkers)
        {
            if (ContainsMarker(comparableCandidate, marker) != ContainsMarker(comparableSource, marker))
            {
                penalty += PenaltyPerVariant;
                penalties.Add($"version:{marker}");
            }
        }

        score -= Math.Min(penalty, PenaltyCap);

        // Duration is the strongest disambiguator for generic titles: a candidate
        // whose length is well off the file is very likely a different recording.
        // Penalise large gaps hard so "same title, wrong length" loses.
        if (source.DurationSeconds > 0 && candidate.DurationSeconds > 0)
        {
            var delta = Math.Abs(source.DurationSeconds - candidate.DurationSeconds);
            if (delta > 15)
            {
                var durationPenalty = Math.Min(0.35, (delta - 15) / 70.0);
                score -= durationPenalty;
                penalties.Add($"duration±{delta}s");
            }
        }

        return new ScoredCandidate(candidate, Math.Clamp(score, 0.0, 1.0), penalties);
    }

    /// <summary>
    /// Fraction of the source artist's significant tokens (≥3 chars) that appear
    /// as whole tokens in the candidate title — catches the remixer-in-title case.
    /// </summary>
    private static double ArtistTokensInTitle(string sourceArtist, string candidateTitle)
    {
        var artistTokens = sourceArtist
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3)
            .ToArray();
        if (artistTokens.Length == 0)
        {
            return 0;
        }

        var titleTokens = candidateTitle
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var matched = artistTokens.Count(titleTokens.Contains);
        return (double)matched / artistTokens.Length;
    }

    private static readonly System.Text.RegularExpressions.Regex RemixWord =
        new(@"\brmx\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Treats "rmx" as "remix" so a remix source isn't penalized against a remix candidate.</summary>
    private static string NormalizeRemix(string text) => RemixWord.Replace(text, "remix");

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
