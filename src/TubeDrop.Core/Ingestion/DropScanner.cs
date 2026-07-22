namespace TubeDrop.Core.Ingestion;

/// <summary>Result of expanding a set of dropped paths (files and/or folders).</summary>
/// <param name="AudioFiles">Absolute paths of accepted audio files, in discovery order.</param>
/// <param name="SkippedCount">Non-audio files encountered (counted, never an error).</param>
/// <param name="MissingCount">Dropped paths that no longer exist on disk.</param>
public sealed record DropScanResult(IReadOnlyList<string> AudioFiles, int SkippedCount, int MissingCount)
{
    public static readonly DropScanResult Empty = new([], 0, 0);
}

/// <summary>
/// Expands dropped files/folders into the flat list of audio files to ingest.
/// Folders are scanned recursively; duplicates (same full path dropped twice,
/// or a file dropped alongside its parent folder) are collapsed.
/// </summary>
public static class DropScanner
{
    public static DropScanResult Scan(IEnumerable<string> droppedPaths)
    {
        var audio = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0, missing = 0;

        foreach (var path in droppedPaths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in EnumerateFilesSafe(path))
                {
                    Classify(file, audio, seen, ref skipped);
                }
            }
            else if (File.Exists(path))
            {
                Classify(path, audio, seen, ref skipped);
            }
            else
            {
                missing++;
            }
        }

        return new DropScanResult(audio, skipped, missing);
    }

    private static void Classify(string file, List<string> audio, HashSet<string> seen, ref int skipped)
    {
        var full = Path.GetFullPath(file);
        if (!seen.Add(full))
        {
            return;
        }

        if (AudioFileTypes.IsAudio(full))
        {
            audio.Add(full);
        }
        else
        {
            skipped++;
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        // Unreadable subdirectories must not abort the whole scan.
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        return Directory.EnumerateFiles(root, "*", options);
    }
}
