namespace TubeDrop.Core.Journal;

/// <summary>Operation type names as stored in the journal.</summary>
public static class OperationTypes
{
    public const string AddItem = "add_item";
    public const string RemoveItem = "remove_item";
    public const string CreatePlaylist = "create_playlist";
    public const string DeletePlaylist = "delete_playlist";
}

// Payload / inverse shapes serialized into the journal (§10).

public sealed record AddItemPayload(string PlaylistId, string VideoId, string? SetVideoId);

public sealed record RemoveItemPayload(string PlaylistId, string SetVideoId, string VideoId);

public sealed record CreatePlaylistPayload(string PlaylistId, string Title, string Description, string Privacy);

public sealed record DeletePlaylistPayload(string PlaylistId, long SnapshotId);

/// <summary>Inverse of delete: rebuild from a snapshot (new playlistId — YouTube has no trash).</summary>
public sealed record RecreateFromSnapshotPayload(long SnapshotId);
