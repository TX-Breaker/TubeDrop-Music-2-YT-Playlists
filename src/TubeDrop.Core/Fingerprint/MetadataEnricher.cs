using Microsoft.Extensions.Logging;
using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Settings;

namespace TubeDrop.Core.Fingerprint;

/// <summary>Improves a track's artist/title before matching (e.g. by audio fingerprint).</summary>
public interface IMetadataEnricher
{
    Task<TrackInfo> EnrichAsync(TrackInfo track, CancellationToken ct = default);
}

/// <summary>Enrichment disabled — returns the track unchanged.</summary>
public sealed class NullMetadataEnricher : IMetadataEnricher
{
    public Task<TrackInfo> EnrichAsync(TrackInfo track, CancellationToken ct = default) => Task.FromResult(track);
}

/// <summary>
/// "Shazam-style" recognition (§8-adjacent): when a track's metadata is weak
/// (no artist, or derived from the filename), fingerprint the audio with
/// Chromaprint and look it up on AcoustID to recover the real artist + title,
/// which then drive the YouTube query. Opt-in (needs an AcoustID key and the
/// fpcalc tool); degrades to the original track on anything missing or uncertain.
/// </summary>
public sealed class AcoustIdEnricher(
    IAudioFingerprinter fingerprinter,
    IAcoustIdClient acoustId,
    ISettingsStore settings,
    ILogger<AcoustIdEnricher> logger) : IMetadataEnricher
{
    private const double MinScore = 0.5;

    public async Task<TrackInfo> EnrichAsync(TrackInfo track, CancellationToken ct = default)
    {
        var s = settings.Current;
        if (!s.AcoustIdEnabled || string.IsNullOrWhiteSpace(s.AcoustIdApiKey) || !fingerprinter.IsAvailable)
        {
            return track;
        }

        // Only spend an API call on tracks whose metadata is actually weak.
        var weak = string.IsNullOrWhiteSpace(track.Artist) || track.Origin == TrackMetadataOrigin.FilenameHeuristics;
        if (!weak)
        {
            return track;
        }

        try
        {
            var fingerprint = await fingerprinter.ComputeAsync(track.SourcePath, ct).ConfigureAwait(false);
            if (fingerprint is null)
            {
                return track;
            }

            var match = await acoustId.LookupAsync(fingerprint, s.AcoustIdApiKey, ct).ConfigureAwait(false);
            if (match is null || match.Score < MinScore ||
                match.Title.Length == 0 || match.Artist.Length == 0)
            {
                return track;
            }

            logger.LogInformation("AcoustID recognized {Path} as {Artist} — {Title} (score {Score:0.00})",
                track.SourcePath, match.Artist, match.Title, match.Score);

            return track with
            {
                Artist = match.Artist,
                Title = match.Title,
                Origin = TrackMetadataOrigin.Tags, // treat recognized metadata as authoritative
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AcoustID enrichment failed for {Path}", track.SourcePath);
            return track;
        }
    }
}
