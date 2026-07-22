using Microsoft.Extensions.Logging;
using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Journal;
using TubeDrop.Core.Matching;
using TubeDrop.Core.Playlists;
using TubeDrop.Core.Settings;

namespace TubeDrop.App.Services;

public enum TrackPhase
{
    Pending,
    Searching,
    Matched,
    Adding,
    Added,
    Unmatched,
    Error,
}

/// <summary>Live state of one track through the batch — bound by the Queue view.</summary>
public sealed class TrackProgress(TrackInfo track)
{
    /// <summary>Mutable so enrichment (fingerprint recognition) can refine it before matching.</summary>
    public TrackInfo Track { get; set; } = track;
    public TrackPhase Phase { get; set; } = TrackPhase.Pending;
    public TrackMatchResult? Match { get; set; }
    public string? Message { get; set; }

    /// <summary>setVideoId returned when the track was added — needed for journaled per-row removal (§11).</summary>
    public string? AddedSetVideoId { get; set; }
}

public sealed record BatchTarget(
    bool CreateNew,
    string? ExistingPlaylistId,
    string NewTitle,
    string NewDescription,
    PlaylistPrivacy Privacy);

public sealed record BatchOutcome(
    string? PlaylistId,
    IReadOnlyList<TrackProgress> Tracks,
    IngestResult Ingest);

/// <summary>
/// Runs the full pipeline (§7, §9, §10): match each track, create/append the
/// playlist, add matches in chunks, reporting per-track progress. Every add is
/// journaled and undoable via the services it delegates to.
/// </summary>
public sealed class BatchCoordinator(
    MatchingEngine matchingEngine,
    JournaledPlaylistService playlistService,
    ISettingsStore settings,
    TubeDrop.Core.Fingerprint.IMetadataEnricher enricher,
    ILogger<BatchCoordinator> logger)
{
    public async Task<BatchOutcome> RunAsync(
        IngestResult ingest,
        BatchTarget target,
        IReadOnlyList<TrackProgress> progressItems,
        IProgress<TrackProgress>? report = null,
        CancellationToken ct = default)
    {
        var options = new MatchingOptions
        {
            Threshold = settings.Current.ScoreThreshold,
            Scope = settings.Current.SearchScope,
            AggressiveMode = settings.Current.AggressiveMode,
        };

        playlistService.BeginSession();

        // 1) Match everything first (§7) — matched videoIds feed the playlist.
        var matched = new List<TrackProgress>();
        foreach (var item in progressItems)
        {
            ct.ThrowIfCancellationRequested();
            item.Phase = TrackPhase.Searching;
            report?.Report(item);

            try
            {
                // Recognize the real artist/title from audio when metadata is weak (opt-in).
                item.Track = await enricher.EnrichAsync(item.Track, ct).ConfigureAwait(false);

                var result = await matchingEngine.MatchAsync(item.Track, options, ct).ConfigureAwait(false);
                item.Match = result;
                if (result.Status is MatchStatus.AutoMatched or MatchStatus.FallbackMatched or MatchStatus.AggressiveMatched
                    && result.Best is not null)
                {
                    item.Phase = TrackPhase.Matched;
                    matched.Add(item);
                }
                else
                {
                    item.Phase = TrackPhase.Unmatched;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                item.Phase = TrackPhase.Error;
                item.Message = ex.Message;
                logger.LogWarning(ex, "Match failed for {Title}", item.Track.Title);
            }

            report?.Report(item);
        }

        if (matched.Count == 0)
        {
            logger.LogInformation("Batch produced no matches — nothing to add");
            return new BatchOutcome(null, progressItems, ingest);
        }

        // 2) Create or resolve the target playlist (§9).
        var playlistId = target.CreateNew
            ? await playlistService.CreatePlaylistAsync(
                target.NewTitle, target.NewDescription, target.Privacy, ct).ConfigureAwait(false)
            : target.ExistingPlaylistId!;

        // 3) Add matched videos in chunks with progress (§9, §10).
        var byVideoId = matched
            .GroupBy(m => m.Match!.Best!.Candidate.VideoId)
            .ToDictionary(g => g.Key, g => g.First());
        var videoIds = byVideoId.Keys.ToList();

        foreach (var item in matched)
        {
            item.Phase = TrackPhase.Adding;
            report?.Report(item);
        }

        var added = await playlistService.AddItemsAsync(playlistId, videoIds, (processed, total, videoId, ok) =>
        {
            if (byVideoId.TryGetValue(videoId, out var item))
            {
                item.Phase = ok ? TrackPhase.Added : TrackPhase.Error;
                item.Message = ok ? null : "YouTube did not confirm the add";
                report?.Report(item);
            }
        }, ct).ConfigureAwait(false);

        // Attach setVideoIds so the report can offer journaled per-row removal (§11).
        foreach (var addedItem in added)
        {
            if (addedItem.SetVideoId is not null && byVideoId.TryGetValue(addedItem.VideoId, out var item))
            {
                item.AddedSetVideoId = addedItem.SetVideoId;
            }
        }

        logger.LogInformation("Batch added {Count} tracks to {PlaylistId}", videoIds.Count, playlistId);
        return new BatchOutcome(playlistId, progressItems, ingest);
    }
}
