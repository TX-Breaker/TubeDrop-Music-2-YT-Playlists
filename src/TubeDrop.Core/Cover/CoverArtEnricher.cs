using Microsoft.Extensions.Logging;
using TubeDrop.Core.Fingerprint;
using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Settings;

namespace TubeDrop.Core.Cover;

/// <summary>
/// Recognizes the artist from the embedded cover art BEFORE searching, so a
/// no-artist track searches "RealArtist Title" instead of a bare generic title.
/// Reverse-image-searches the cover (keyless, via the Google session), extracts
/// an artist from the result text, and fills it in. Runs only for weak-metadata
/// tracks with a cover; degrades to the original track on anything uncertain.
/// </summary>
public sealed class CoverArtEnricher(
    ICoverImageLookup lookup,
    ISettingsStore settings,
    ILogger<CoverArtEnricher> logger) : IMetadataEnricher
{
    public async Task<TrackInfo> EnrichAsync(TrackInfo track, CancellationToken ct = default)
    {
        if (!settings.Current.CoverSearchEnabled || !lookup.IsAvailable ||
            track.CoverArt is not { Length: > 0 } ||
            !string.IsNullOrWhiteSpace(track.Artist)) // only when we still lack an artist
        {
            return track;
        }

        try
        {
            var result = await lookup.LookupAsync(track.CoverArt, ct).ConfigureAwait(false);
            if (result.IsEmpty)
            {
                return track;
            }

            var artist = CoverArtistExtractor.Extract(result.BestGuess, result.Suggestions, track.Title);
            if (string.IsNullOrWhiteSpace(artist))
            {
                return track;
            }

            logger.LogInformation("Cover recognized artist for {Path}: {Artist}", track.SourcePath, artist);
            return track with { Artist = artist };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cover artist recognition failed for {Path}", track.SourcePath);
            return track;
        }
    }
}
