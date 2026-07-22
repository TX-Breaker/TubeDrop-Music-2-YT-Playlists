using System.Globalization;
using System.Text;

namespace TubeDrop.Core.Ingestion;

/// <summary>Outcome of ingesting one drop: unique tracks plus §6 counters.</summary>
public sealed record IngestResult(
    IReadOnlyList<TrackInfo> Tracks,
    int DuplicateCount,
    int SkippedCount,
    int MissingCount,
    int ErrorCount);

public interface IIngestPipeline
{
    IngestResult Ingest(IReadOnlyList<string> droppedPaths, CancellationToken cancellationToken = default);
}

/// <summary>
/// Drop → scan → tags/heuristics → in-batch dedup (§6): same normalized
/// artist+title with duration within ±2 s collapses to one entry.
/// </summary>
public sealed class IngestPipeline(ITagReader tagReader) : IIngestPipeline
{
    private const int DuplicateDurationToleranceSeconds = 2;

    public IngestResult Ingest(IReadOnlyList<string> droppedPaths, CancellationToken cancellationToken = default)
    {
        var scan = DropScanner.Scan(droppedPaths);
        var tracks = new List<TrackInfo>();
        var byKey = new Dictionary<string, List<TrackInfo>>();
        int duplicates = 0, errors = 0;

        foreach (var file in scan.AudioFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var track = tagReader.Read(file);
            if (track is null)
            {
                errors++;
                continue;
            }

            var key = NormalizeKey(track.Artist, track.Title);
            if (byKey.TryGetValue(key, out var existing) &&
                existing.Any(t => Math.Abs(t.DurationSeconds - track.DurationSeconds) <= DuplicateDurationToleranceSeconds))
            {
                duplicates++;
                continue;
            }

            (byKey.TryGetValue(key, out var list) ? list : byKey[key] = []).Add(track);
            tracks.Add(track);
        }

        return new IngestResult(tracks, duplicates, scan.SkippedCount, scan.MissingCount, errors);
    }

    /// <summary>Case-, accent- and punctuation-insensitive identity for dedup.</summary>
    internal static string NormalizeKey(string artist, string title) =>
        $"{Normalize(artist)}|{Normalize(title)}";

    private static string Normalize(string value)
    {
        var lowered = FilenameHeuristics.CleanNoise(value).ToLowerInvariant();
        var formD = lowered.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue; // strip accents
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c) && sb.Length > 0 && sb[^1] != ' ')
            {
                sb.Append(' ');
            }
        }

        return sb.ToString().Trim();
    }
}
