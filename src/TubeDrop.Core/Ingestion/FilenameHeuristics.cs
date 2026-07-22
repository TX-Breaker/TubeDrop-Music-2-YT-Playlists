using System.Text.RegularExpressions;

namespace TubeDrop.Core.Ingestion;

/// <summary>
/// Derives (artist, title) from a file name when tags are missing (§6).
/// Recognized shapes, tried in order:
///   "NN - Artist - Title", "NN. Title", "Artist - Title", bare title.
/// Underscores/dots used as separators are normalized to spaces first, and
/// release-noise suffixes like "(Official Video)" are stripped.
/// </summary>
public static partial class FilenameHeuristics
{
    [GeneratedRegex(@"^\s*(\d{1,3})\s*[-.]\s+(.+)$")]
    private static partial Regex LeadingTrackNumber();

    [GeneratedRegex(@"[\(\[\{]\s*(official\s+(music\s+)?(video|audio|visualizer|lyric(s)?(\s+video)?)|official|videoclip(\s+ufficiale)?|video\s+ufficiale|lyric(s)?(\s+video)?|audio(\s+only)?|visualizer|hd|hq|4k|full\s+(song|album)|explicit|clean|napisy.*|sub(title[sd]?|bed|s)?(\s+\w+)?|con\s+testo|testo|paroles|letra|mv|m/v|pv)\s*[\)\]\}]", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseBrackets();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    public static (string Artist, string Title, int TrackNumber) Parse(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        // Underscore-separated names ("Artist_-_Title", "My_Song") → spaces.
        if (name.Contains('_'))
        {
            name = name.Replace('_', ' ');
        }

        // Dot-separated names ("Artist.-.Title", "Some.Song.Name") → spaces,
        // but only when dots clearly act as separators (no spaces present at all).
        if (!name.Contains(' ') && name.Count(c => c == '.') >= 2)
        {
            name = name.Replace('.', ' ');
        }

        name = CleanNoise(name);

        var trackNumber = 0;
        var match = LeadingTrackNumber().Match(name);
        if (match.Success)
        {
            trackNumber = int.Parse(match.Groups[1].Value);
            name = match.Groups[2].Value.Trim();
        }

        // "Artist - Title" (first separator wins; artists rarely contain " - ")
        var separatorIndex = name.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            var artist = name[..separatorIndex].Trim();
            var title = name[(separatorIndex + 3)..].Trim();
            if (artist.Length > 0 && title.Length > 0)
            {
                return (artist, title, trackNumber);
            }
        }

        return ("", name.Trim(), trackNumber);
    }

    /// <summary>Strips "(Official Video)"-style noise; shared with query building later (§7).</summary>
    public static string CleanNoise(string value)
    {
        var cleaned = NoiseBrackets().Replace(value, " ");
        cleaned = MultiSpace().Replace(cleaned, " ");
        return cleaned.Trim();
    }
}
