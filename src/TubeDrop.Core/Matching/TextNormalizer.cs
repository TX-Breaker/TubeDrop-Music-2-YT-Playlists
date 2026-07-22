using AnyAscii;
using TubeDrop.Core.Ingestion;

namespace TubeDrop.Core.Matching;

/// <summary>Shared text preparation for fuzzy comparison and query building.</summary>
public static class TextNormalizer
{
    /// <summary>Lowercase, noise-stripped, transliterated to ASCII (AnyAscii).</summary>
    public static string ForComparison(string value)
    {
        var cleaned = FilenameHeuristics.CleanNoise(value).ToLowerInvariant();
        return cleaned.Transliterate().Trim();
    }

    /// <summary>AnyAscii transliteration for non-Latin scripts (§8.1); unchanged when already Latin.</summary>
    public static string Transliterated(string value) => value.Transliterate();

    /// <summary>True when the string contains characters outside the Latin/ASCII range.</summary>
    public static bool HasNonLatin(string value) => value.Any(c => c > 0x024F);
}
