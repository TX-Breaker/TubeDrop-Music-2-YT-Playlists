using System.Text.Json;
using TubeDrop.Core.Playlists;

namespace TubeDrop.Core.Journal;

/// <summary>Progress callback for batch adds: (processed, total, videoId, succeeded).</summary>
public delegate void AddProgress(int processed, int total, string videoId, bool succeeded);

/// <summary>
/// The ONLY allowed path for playlist mutations (§10): every operation is
/// journaled together with its inverse BEFORE the network call executes.
/// Adds are chunked (§9); remove/delete snapshot state first.
/// </summary>
public sealed class JournaledPlaylistService(JournalStore journal, IPlaylistClient client)
{
    public const int AddChunkSize = 20;

    public long CurrentSessionId { get; private set; }

    public long BeginSession() => CurrentSessionId = journal.BeginSession();

    public async Task<string> CreatePlaylistAsync(
        string title, string description, PlaylistPrivacy privacy, CancellationToken ct = default)
    {
        EnsureSession();

        // Journal first with a placeholder id, patch after YouTube answers.
        var operationId = journal.RecordOperation(CurrentSessionId, OperationTypes.CreatePlaylist,
            new CreatePlaylistPayload("", title, description, privacy.ToString()),
            new DeletePlaylistPayload("", -1));

        string playlistId;
        try
        {
            playlistId = await client.CreatePlaylistAsync(title, description, privacy, null, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            journal.SetStatus(operationId, OperationStatus.Failed);
            throw;
        }

        journal.UpdatePayloadAndInverse(operationId,
            new CreatePlaylistPayload(playlistId, title, description, privacy.ToString()),
            new DeletePlaylistPayload(playlistId, -1));
        journal.SetStatus(operationId, OperationStatus.Done);
        return playlistId;
    }

    /// <summary>Adds videos in chunks of <see cref="AddChunkSize"/>; one journal op per video (undo granularity §10).</summary>
    public async Task<IReadOnlyList<AddedItem>> AddItemsAsync(
        string playlistId,
        IReadOnlyList<string> videoIds,
        AddProgress? progress = null,
        CancellationToken ct = default)
    {
        EnsureSession();
        var added = new List<AddedItem>();
        var processed = 0;

        foreach (var chunk in videoIds.Chunk(AddChunkSize))
        {
            ct.ThrowIfCancellationRequested();

            var operationIds = new List<long>();
            foreach (var videoId in chunk)
            {
                operationIds.Add(journal.RecordOperation(CurrentSessionId, OperationTypes.AddItem,
                    new AddItemPayload(playlistId, videoId, null),
                    new RemoveItemPayload(playlistId, "", videoId)));
            }

            IReadOnlyList<AddedItem> chunkResult;
            try
            {
                chunkResult = await client.AddItemsAsync(playlistId, chunk, ct).ConfigureAwait(false);
            }
            catch
            {
                foreach (var id in operationIds)
                {
                    journal.SetStatus(id, OperationStatus.Failed);
                }

                throw;
            }

            for (var i = 0; i < chunk.Length; i++)
            {
                var videoId = chunk[i];
                var result = chunkResult.FirstOrDefault(r => r.VideoId == videoId)
                             ?? (i < chunkResult.Count ? chunkResult[i] : null);
                var setVideoId = result?.SetVideoId;

                if (setVideoId is not null)
                {
                    journal.UpdatePayloadAndInverse(operationIds[i],
                        new AddItemPayload(playlistId, videoId, setVideoId),
                        new RemoveItemPayload(playlistId, setVideoId, videoId));
                    journal.SetStatus(operationIds[i], OperationStatus.Done);
                    added.Add(new AddedItem(videoId, setVideoId));
                }
                else
                {
                    // No setVideoId returned → we cannot undo precisely; mark failed
                    // rather than pretending (§15 no fake success).
                    journal.SetStatus(operationIds[i], OperationStatus.Failed);
                }

                processed++;
                progress?.Invoke(processed, videoIds.Count, videoId, setVideoId is not null);
            }
        }

        return added;
    }

    public async Task RemoveItemsAsync(
        string playlistId, IReadOnlyList<PlaylistItem> items, CancellationToken ct = default)
    {
        EnsureSession();

        var operationIds = new List<long>();
        foreach (var item in items)
        {
            operationIds.Add(journal.RecordOperation(CurrentSessionId, OperationTypes.RemoveItem,
                new RemoveItemPayload(playlistId, item.SetVideoId, item.VideoId),
                new AddItemPayload(playlistId, item.VideoId, null)));
        }

        try
        {
            await client.RemoveItemsAsync(playlistId, items, ct).ConfigureAwait(false);
        }
        catch
        {
            foreach (var id in operationIds)
            {
                journal.SetStatus(id, OperationStatus.Failed);
            }

            throw;
        }

        foreach (var id in operationIds)
        {
            journal.SetStatus(id, OperationStatus.Done);
        }
    }

    public async Task DeletePlaylistAsync(
        string playlistId, string title, string description, PlaylistPrivacy privacy,
        CancellationToken ct = default)
    {
        EnsureSession();

        // Snapshot state BEFORE journaling the delete (§10).
        var items = await client.GetPlaylistItemsAsync(playlistId, ct).ConfigureAwait(false);
        var snapshotId = journal.SaveSnapshot(playlistId, title, description, privacy.ToString(),
            JsonSerializer.Serialize(items));

        var operationId = journal.RecordOperation(CurrentSessionId, OperationTypes.DeletePlaylist,
            new DeletePlaylistPayload(playlistId, snapshotId),
            new RecreateFromSnapshotPayload(snapshotId));

        try
        {
            await client.DeletePlaylistAsync(playlistId, ct).ConfigureAwait(false);
        }
        catch
        {
            journal.SetStatus(operationId, OperationStatus.Failed);
            throw;
        }

        journal.SetStatus(operationId, OperationStatus.Done);
    }

    private void EnsureSession()
    {
        if (CurrentSessionId == 0)
        {
            BeginSession();
        }
    }
}
