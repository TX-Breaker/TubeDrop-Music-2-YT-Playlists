using FuzzySharp;
using TubeDrop.Core.Matching;

namespace TubeDrop.Core.Cover;

/// <summary>
/// Best-effort extraction of an ARTIST name from the free text a reverse-image
/// search returned for a cover, given the title we already know. The cover text
/// is usually something like "Artist - Title", "Title by Artist", or
/// "Artist Title Album" — so the artist is "the part that isn't the title".
/// </summary>
public static class CoverArtistExtractor
{
    private static readonly string[] Separators = [" - ", " – ", " — ", " by ", " · ", " | "];

    public static string? Extract(string bestGuess, IReadOnlyList<string> suggestions, string title)
    {
        var fromBest = FromText(bestGuess, title);
        if (fromBest is not null)
        {
            return fromBest;
        }

        foreach (var suggestion in suggestions)
        {
            var fromSuggestion = FromText(suggestion, title);
            if (fromSuggestion is not null)
            {
                return fromSuggestion;
            }
        }

        return null;
    }

    private static string? FromText(string text, string title)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
        {
            return null;
        }

        // 1) Explicit separators: pick the side least similar to the title.
        foreach (var sep in Separators)
        {
            var idx = text.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (idx <= 0)
            {
                continue;
            }

            var left = text[..idx].Trim();
            var right = text[(idx + sep.Length)..].Trim();
            var candidate = SimilarToTitle(left, title) ? right : left;
            if (IsPlausibleArtist(candidate, title))
            {
                return candidate;
            }
        }

        // 2) No separator: remove the title's words, keep what's left as the artist.
        var remainder = RemoveTitleWords(text, title);
        return IsPlausibleArtist(remainder, title) ? remainder : null;
    }

    private static string RemoveTitleWords(string text, string title)
    {
        var titleWords = TextNormalizer.ForComparison(title)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

        var kept = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !titleWords.Contains(TextNormalizer.ForComparison(w)))
            .ToArray();
        return string.Join(' ', kept).Trim();
    }

    private static bool SimilarToTitle(string value, string title) =>
        Fuzz.TokenSetRatio(TextNormalizer.ForComparison(value), TextNormalizer.ForComparison(title)) >= 80;

    private static bool IsPlausibleArtist(string value, string title)
    {
        value = value.Trim();
        if (value.Length is < 2 or > 40)
        {
            return false;
        }

        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 5)
        {
            return false;
        }

        // Must contain letters, and must not just be the title again.
        return value.Any(char.IsLetter) && !SimilarToTitle(value, title);
    }
}
