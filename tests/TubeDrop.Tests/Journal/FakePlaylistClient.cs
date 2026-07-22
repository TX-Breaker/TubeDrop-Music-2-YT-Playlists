using TubeDrop.Core.Playlists;

namespace TubeDrop.Tests.Journal;

/// <summary>In-memory playlist backend for journal/undo tests.</summary>
public sealed class FakePlaylistClient : IPlaylistClient
{
    private int _playlistSeq;
    private int _setVideoSeq;

    public Dictionary<string, List<PlaylistItem>> Playlists { get; } = new();
    public List<string> DeletedPlaylists { get; } = [];

    /// <summary>When true, AddItemsAsync returns items without a setVideoId (simulates schema drift).</summary>
    public bool SuppressSetVideoIds { get; set; }

    /// <summary>VideoIds that make an add call throw (simulates YouTube's 409 on a bad video).</summary>
    public HashSet<string> FailVideoIds { get; } = [];

    public Task<string> CreatePlaylistAsync(
        string title, string description, PlaylistPrivacy privacy,
        IReadOnlyList<string>? initialVideoIds = null, CancellationToken ct = default)
    {
        var id = $"PL{++_playlistSeq}";
        Playlists[id] = [];
        if (initialVideoIds is not null)
        {
            foreach (var v in initialVideoIds)
            {
                Playlists[id].Add(new PlaylistItem(v, $"SET{++_setVideoSeq}"));
            }
        }

        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<AddedItem>> AddItemsAsync(
        string playlistId, IReadOnlyList<string> videoIds, CancellationToken ct = default)
    {
        // A single bad video makes the whole call fail — like YouTube's 409.
        if (videoIds.Any(FailVideoIds.Contains))
        {
            throw new InvalidOperationException("HTTP 409 (simulated)");
        }

        var list = Playlists.TryGetValue(playlistId, out var existing) ? existing : Playlists[playlistId] = [];
        var added = new List<AddedItem>();
        foreach (var videoId in videoIds)
        {
            var setVideoId = SuppressSetVideoIds ? null : $"SET{++_setVideoSeq}";
            if (setVideoId is not null)
            {
                list.Add(new PlaylistItem(videoId, setVideoId));
            }

            added.Add(new AddedItem(videoId, setVideoId));
        }

        return Task.FromResult<IReadOnlyList<AddedItem>>(added);
    }

    public Task RemoveItemsAsync(string playlistId, IReadOnlyList<PlaylistItem> items, CancellationToken ct = default)
    {
        if (Playlists.TryGetValue(playlistId, out var list))
        {
            foreach (var item in items)
            {
                list.RemoveAll(x => x.SetVideoId == item.SetVideoId);
            }
        }

        return Task.CompletedTask;
    }

    public Task DeletePlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        Playlists.Remove(playlistId);
        DeletedPlaylists.Add(playlistId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PlaylistSummary>> GetLibraryPlaylistsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlaylistSummary>>(
            Playlists.Select(p => new PlaylistSummary(p.Key, p.Key, p.Value.Count)).ToList());

    public Task<IReadOnlyList<PlaylistItem>> GetPlaylistItemsAsync(string playlistId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlaylistItem>>(
            Playlists.TryGetValue(playlistId, out var list) ? [.. list] : []);
}
