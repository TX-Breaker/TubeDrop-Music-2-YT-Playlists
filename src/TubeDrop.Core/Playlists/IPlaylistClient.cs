namespace TubeDrop.Core.Playlists;

public enum PlaylistPrivacy
{
    Private,
    Unlisted,
    Public,
}

public sealed record PlaylistSummary(string PlaylistId, string Title, int ItemCount);

/// <summary>One playlist entry; SetVideoId is required for precise removal/undo (§5).</summary>
public sealed record PlaylistItem(string VideoId, string SetVideoId);

/// <summary>Result of adding one video; SetVideoId null when YouTube did not return one.</summary>
public sealed record AddedItem(string VideoId, string? SetVideoId);

/// <summary>
/// Playlist mutations + library reads, implemented by the InnerTube layer.
/// Core services must only mutate through <see cref="Journal.JournaledPlaylistService"/>.
/// </summary>
public interface IPlaylistClient
{
    Task<string> CreatePlaylistAsync(
        string title, string description, PlaylistPrivacy privacy,
        IReadOnlyList<string>? initialVideoIds = null, CancellationToken ct = default);

    Task<IReadOnlyList<AddedItem>> AddItemsAsync(
        string playlistId, IReadOnlyList<string> videoIds, CancellationToken ct = default);

    Task RemoveItemsAsync(
        string playlistId, IReadOnlyList<PlaylistItem> items, CancellationToken ct = default);

    Task DeletePlaylistAsync(string playlistId, CancellationToken ct = default);

    Task<IReadOnlyList<PlaylistSummary>> GetLibraryPlaylistsAsync(CancellationToken ct = default);

    /// <summary>Full item list of a playlist — used for pre-mutation snapshots (§10).</summary>
    Task<IReadOnlyList<PlaylistItem>> GetPlaylistItemsAsync(string playlistId, CancellationToken ct = default);
}
