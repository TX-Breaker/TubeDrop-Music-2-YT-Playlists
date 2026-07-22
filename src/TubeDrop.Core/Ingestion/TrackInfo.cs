namespace TubeDrop.Core.Ingestion;

/// <summary>Where the artist/title of a track came from.</summary>
public enum TrackMetadataOrigin
{
    /// <summary>Read from embedded tags.</summary>
    Tags,

    /// <summary>Derived from the file name because tags were missing/empty (§6).</summary>
    FilenameHeuristics,
}

/// <summary>Normalized metadata for one ingested audio file.</summary>
public sealed record TrackInfo
{
    public required string SourcePath { get; init; }
    public required string Title { get; init; }
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string AlbumArtist { get; init; } = "";
    public int DurationSeconds { get; init; }
    public int TrackNumber { get; init; }
    public int Year { get; init; }
    public string Genre { get; init; } = "";
    public TrackMetadataOrigin Origin { get; init; } = TrackMetadataOrigin.Tags;

    /// <summary>Embedded cover art (first picture); UI display + optional reverse-image lookup.</summary>
    public byte[]? CoverArt { get; init; }
}
