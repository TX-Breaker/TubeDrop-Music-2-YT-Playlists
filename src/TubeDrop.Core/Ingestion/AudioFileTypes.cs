namespace TubeDrop.Core.Ingestion;

/// <summary>
/// Audio container/codec extensions accepted for ingestion.
/// Mirrors the formats supported by ATL (atldotnet); anything else is
/// counted as "skipped", never surfaced as an error (§6 of the spec).
/// </summary>
public static class AudioFileTypes
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".mp2", ".mp1",
        ".flac",
        ".m4a", ".m4b", ".mp4", ".aac",
        ".ogg", ".oga", ".opus", ".spx",
        ".wav", ".wave", ".bwf",
        ".wma",
        ".aif", ".aiff", ".aifc",
        ".ape",
        ".mpc", ".mp+",
        ".wv",
        ".dsf", ".dff",
        ".tak",
        ".tta",
        ".ofr", ".ofs",
        ".vqf",
        ".gym", ".vgm", ".vgz",
        ".psf", ".psf2", ".minipsf", ".minipsf2", ".ssf",
        ".s3m", ".xm", ".it", ".mod",
        ".mid", ".midi",
        ".caf",
        ".aa", ".aax",
        ".ac3",
    };

    public static bool IsAudio(string path) =>
        Extensions.Contains(Path.GetExtension(path));
}
