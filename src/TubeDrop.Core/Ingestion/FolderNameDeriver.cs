namespace TubeDrop.Core.Ingestion;

public enum FolderNameMode
{
    /// <summary>Name of the dropped folder itself (or common root of multiple drops).</summary>
    Master,

    /// <summary>Deepest common subfolder actually containing the audio files.</summary>
    Subfolder,
}

/// <param name="Name">Derived playlist name, or null when underivable (loose files only).</param>
/// <param name="FellBackToMaster">True when Subfolder was requested but tracks span multiple subfolders (§9) — noted in the report.</param>
public sealed record FolderNameResult(string? Name, bool FellBackToMaster);

/// <summary>Implements the folder-based playlist naming semantics of §9.</summary>
public static class FolderNameDeriver
{
    /// <param name="droppedPaths">The paths as originally dropped (files and/or folders).</param>
    /// <param name="audioFiles">The audio files those drops expanded to.</param>
    public static FolderNameResult Derive(
        IReadOnlyList<string> droppedPaths,
        IReadOnlyList<string> audioFiles,
        FolderNameMode mode)
    {
        if (audioFiles.Count == 0)
        {
            return new FolderNameResult(null, false);
        }

        var droppedFolders = droppedPaths.Where(Directory.Exists).Select(Path.GetFullPath).ToList();

        // Loose files with no folder involved → toggle auto-disables (§9).
        if (droppedFolders.Count == 0)
        {
            return new FolderNameResult(null, false);
        }

        var masterName = MasterName(droppedFolders);
        if (mode == FolderNameMode.Master)
        {
            return new FolderNameResult(masterName, false);
        }

        // Subfolder: deepest common ancestor directory of all audio files.
        var commonDir = CommonDirectory(audioFiles.Select(f => Path.GetDirectoryName(Path.GetFullPath(f))!));
        if (commonDir is null)
        {
            return new FolderNameResult(masterName, true);
        }

        // Tracks directly spread across sibling subfolders → their common dir is
        // the master (or above): that means "multiple distinct subfolders" → fall back.
        var masterDir = droppedFolders.Count == 1
            ? droppedFolders[0]
            : CommonDirectory(droppedFolders);
        if (masterDir is not null &&
            commonDir.TrimEnd(Path.DirectorySeparatorChar).Length <= masterDir.TrimEnd(Path.DirectorySeparatorChar).Length)
        {
            return new FolderNameResult(masterName, true);
        }

        return new FolderNameResult(Path.GetFileName(commonDir.TrimEnd(Path.DirectorySeparatorChar)), false);
    }

    private static string MasterName(IReadOnlyList<string> droppedFolders)
    {
        if (droppedFolders.Count == 1)
        {
            return Path.GetFileName(droppedFolders[0].TrimEnd(Path.DirectorySeparatorChar));
        }

        var common = CommonDirectory(droppedFolders);
        return common is not null
            ? Path.GetFileName(common.TrimEnd(Path.DirectorySeparatorChar))
            : Path.GetFileName(droppedFolders[0].TrimEnd(Path.DirectorySeparatorChar));
    }

    /// <summary>Deepest directory that is an ancestor of (or equal to) every input directory.</summary>
    internal static string? CommonDirectory(IEnumerable<string> directories)
    {
        string[]? common = null;
        foreach (var dir in directories)
        {
            var parts = Path.GetFullPath(dir)
                .TrimEnd(Path.DirectorySeparatorChar)
                .Split(Path.DirectorySeparatorChar);
            if (common is null)
            {
                common = parts;
                continue;
            }

            var len = 0;
            while (len < common.Length && len < parts.Length &&
                   string.Equals(common[len], parts[len], StringComparison.OrdinalIgnoreCase))
            {
                len++;
            }

            if (len == 0)
            {
                return null; // different drives
            }

            common = common[..len];
        }

        return common is null ? null : string.Join(Path.DirectorySeparatorChar, common);
    }
}
