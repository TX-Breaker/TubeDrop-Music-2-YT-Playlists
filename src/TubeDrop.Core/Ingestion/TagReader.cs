using ATL;

namespace TubeDrop.Core.Ingestion;

/// <summary>Reads embedded tags via ATL, falling back to filename heuristics (§6).</summary>
public interface ITagReader
{
    /// <summary>Returns null when the file cannot be read at all (corrupt/locked).</summary>
    TrackInfo? Read(string path);
}

public sealed class TagReader : ITagReader
{
    public TrackInfo? Read(string path)
    {
        Track track;
        try
        {
            track = new Track(path);
        }
        catch (Exception)
        {
            return null;
        }

        var artist = Clean(track.Artist);
        var title = Clean(track.Title);

        // ATL fills Title with the file name when no tag exists; treat a Title
        // equal to the bare file name as "no real tag" so heuristics can run.
        var bareName = Path.GetFileNameWithoutExtension(path);
        var hasRealTitle = title.Length > 0 && !title.Equals(bareName, StringComparison.OrdinalIgnoreCase);

        if (hasRealTitle)
        {
            return new TrackInfo
            {
                SourcePath = path,
                Title = title,
                Artist = artist,
                Album = Clean(track.Album),
                AlbumArtist = Clean(track.AlbumArtist),
                DurationSeconds = track.Duration,
                TrackNumber = track.TrackNumber ?? 0,
                Year = track.Year ?? 0,
                Origin = TrackMetadataOrigin.Tags,
                CoverArt = track.EmbeddedPictures.FirstOrDefault()?.PictureData,
            };
        }

        var (heuristicArtist, heuristicTitle, trackNumber) = FilenameHeuristics.Parse(path);
        return new TrackInfo
        {
            SourcePath = path,
            Title = heuristicTitle,
            Artist = artist.Length > 0 ? artist : heuristicArtist,
            Album = Clean(track.Album),
            AlbumArtist = Clean(track.AlbumArtist),
            DurationSeconds = track.Duration,
            TrackNumber = track.TrackNumber ?? trackNumber,
            Year = track.Year ?? 0,
            Origin = TrackMetadataOrigin.FilenameHeuristics,
            CoverArt = track.EmbeddedPictures.FirstOrDefault()?.PictureData,
        };
    }

    private static string Clean(string? value) => value?.Trim() ?? "";
}
