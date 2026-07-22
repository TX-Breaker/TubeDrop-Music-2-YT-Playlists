using System.Text.Json;
using TubeDrop.Core.Playlists;

namespace TubeDrop.Core.Journal;

public sealed record UndoOutcome(bool Succeeded, string Message, string? NewPlaylistId = null);

/// <summary>
/// Executes recorded inverses (§10). Undo operations are themselves journaled,
/// so a redo is just an undo of the undo. Recreating a deleted playlist yields
/// a NEW playlistId — surfaced in the outcome so the UI can warn.
/// </summary>
public sealed class UndoService(JournalStore journal, JournaledPlaylistService playlistService)
{
    public async Task<UndoOutcome> UndoOperationAsync(long operationId, CancellationToken ct = default)
    {
        var operation = journal.GetOperation(operationId);
        if (operation is null)
        {
            return new UndoOutcome(false, $"Operation {operationId} not found");
        }

        if (operation.Status != OperationStatus.Done)
        {
            return new UndoOutcome(false, $"Operation {operationId} is {operation.Status} — nothing to undo");
        }

        try
        {
            var outcome = operation.Type switch
            {
                OperationTypes.AddItem => await UndoAddAsync(operation, ct).ConfigureAwait(false),
                OperationTypes.RemoveItem => await UndoRemoveAsync(operation, ct).ConfigureAwait(false),
                OperationTypes.CreatePlaylist => await UndoCreateAsync(operation, ct).ConfigureAwait(false),
                OperationTypes.DeletePlaylist => await UndoDeleteAsync(operation, ct).ConfigureAwait(false),
                _ => new UndoOutcome(false, $"Unknown operation type '{operation.Type}'"),
            };

            if (outcome.Succeeded)
            {
                journal.SetStatus(operationId, OperationStatus.Undone);
            }

            return outcome;
        }
        catch (Exception ex)
        {
            return new UndoOutcome(false, $"Undo failed: {ex.Message}");
        }
    }

    /// <summary>Undoes every Done operation of a session, newest first.</summary>
    public async Task<IReadOnlyList<(long OperationId, UndoOutcome Outcome)>> UndoSessionAsync(
        long sessionId, CancellationToken ct = default)
    {
        var results = new List<(long, UndoOutcome)>();
        foreach (var operation in journal.GetSessionOperations(sessionId).Reverse())
        {
            if (operation.Status != OperationStatus.Done)
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();
            results.Add((operation.Id, await UndoOperationAsync(operation.Id, ct).ConfigureAwait(false)));
        }

        return results;
    }

    private async Task<UndoOutcome> UndoAddAsync(JournalOperation operation, CancellationToken ct)
    {
        var inverse = JsonSerializer.Deserialize<RemoveItemPayload>(operation.InverseJson);
        if (inverse is null || inverse.SetVideoId.Length == 0)
        {
            return new UndoOutcome(false, "No setVideoId recorded — cannot remove precisely");
        }

        await playlistService.RemoveItemsAsync(inverse.PlaylistId,
            [new PlaylistItem(inverse.VideoId, inverse.SetVideoId)], ct).ConfigureAwait(false);
        return new UndoOutcome(true, $"Removed {inverse.VideoId} from {inverse.PlaylistId}");
    }

    private async Task<UndoOutcome> UndoRemoveAsync(JournalOperation operation, CancellationToken ct)
    {
        var inverse = JsonSerializer.Deserialize<AddItemPayload>(operation.InverseJson);
        if (inverse is null)
        {
            return new UndoOutcome(false, "Corrupt inverse payload");
        }

        await playlistService.AddItemsAsync(inverse.PlaylistId, [inverse.VideoId], null, ct)
            .ConfigureAwait(false);
        return new UndoOutcome(true, $"Re-added {inverse.VideoId} to {inverse.PlaylistId}");
    }

    private async Task<UndoOutcome> UndoCreateAsync(JournalOperation operation, CancellationToken ct)
    {
        var inverse = JsonSerializer.Deserialize<DeletePlaylistPayload>(operation.InverseJson);
        if (inverse is null || inverse.PlaylistId.Length == 0)
        {
            return new UndoOutcome(false, "No playlistId recorded");
        }

        var payload = JsonSerializer.Deserialize<CreatePlaylistPayload>(operation.PayloadJson);
        await playlistService.DeletePlaylistAsync(inverse.PlaylistId,
            payload?.Title ?? "", payload?.Description ?? "",
            Enum.TryParse<PlaylistPrivacy>(payload?.Privacy, out var privacy) ? privacy : PlaylistPrivacy.Private,
            ct).ConfigureAwait(false);
        return new UndoOutcome(true, $"Deleted playlist {inverse.PlaylistId}");
    }

    private async Task<UndoOutcome> UndoDeleteAsync(JournalOperation operation, CancellationToken ct)
    {
        var inverse = JsonSerializer.Deserialize<RecreateFromSnapshotPayload>(operation.InverseJson);
        var snapshot = inverse is null ? null : journal.GetSnapshot(inverse.SnapshotId);
        if (snapshot is null)
        {
            return new UndoOutcome(false, "Snapshot not found — cannot rebuild playlist");
        }

        var items = JsonSerializer.Deserialize<List<PlaylistItem>>(snapshot.ItemsJson) ?? [];
        var privacy = Enum.TryParse<PlaylistPrivacy>(snapshot.Privacy, out var p) ? p : PlaylistPrivacy.Private;

        // Rebuild through the journaled path so the recreation is itself undoable.
        var newPlaylistId = await playlistService.CreatePlaylistAsync(
            snapshot.Title, snapshot.Description, privacy, ct).ConfigureAwait(false);
        if (items.Count > 0)
        {
            await playlistService.AddItemsAsync(newPlaylistId,
                items.Select(i => i.VideoId).ToList(), null, ct).ConfigureAwait(false);
        }

        return new UndoOutcome(true,
            $"Rebuilt playlist as {newPlaylistId} (YouTube has no trash — the id is new)", newPlaylistId);
    }
}
