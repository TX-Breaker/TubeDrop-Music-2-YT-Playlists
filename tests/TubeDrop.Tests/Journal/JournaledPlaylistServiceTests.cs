using TubeDrop.Core.Journal;
using TubeDrop.Core.Playlists;

namespace TubeDrop.Tests.Journal;

public sealed class JournaledPlaylistServiceTests : IDisposable
{
    private readonly string _dbDir;
    private readonly JournalStore _store;
    private readonly FakePlaylistClient _client = new();
    private readonly JournaledPlaylistService _service;

    public JournaledPlaylistServiceTests()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), "TubeDropTests", Guid.NewGuid().ToString("N"));
        _store = new JournalStore(Path.Combine(_dbDir, "journal.db"));
        _service = new JournaledPlaylistService(_store, _client);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            Directory.Delete(_dbDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task CreatePlaylist_JournaledWithRealId()
    {
        var sessionId = _service.BeginSession();

        var playlistId = await _service.CreatePlaylistAsync("My List", "desc", PlaylistPrivacy.Private);

        Assert.True(_client.Playlists.ContainsKey(playlistId));
        var ops = _store.GetSessionOperations(sessionId);
        Assert.Single(ops);
        Assert.Equal(OperationTypes.CreatePlaylist, ops[0].Type);
        Assert.Equal(OperationStatus.Done, ops[0].Status);
        Assert.Contains(playlistId, ops[0].PayloadJson);
        Assert.Contains(playlistId, ops[0].InverseJson); // inverse can delete the real id
    }

    [Fact]
    public async Task AddItems_CapturesSetVideoIdInInverse()
    {
        _service.BeginSession();
        var playlistId = await _service.CreatePlaylistAsync("L", "", PlaylistPrivacy.Private);

        var added = await _service.AddItemsAsync(playlistId, ["v1", "v2"]);

        Assert.Equal(2, added.Count);
        Assert.All(added, a => Assert.NotNull(a.SetVideoId));
        // Every add op records a remove-by-setVideoId inverse.
        var addOps = _store.GetSessionOperations(_service.CurrentSessionId)
            .Where(o => o.Type == OperationTypes.AddItem).ToList();
        Assert.Equal(2, addOps.Count);
        Assert.All(addOps, o =>
        {
            Assert.Equal(OperationStatus.Done, o.Status);
            Assert.Contains("SET", o.InverseJson);
        });
    }

    [Fact]
    public async Task AddItems_Chunks()
    {
        _service.BeginSession();
        var playlistId = await _service.CreatePlaylistAsync("L", "", PlaylistPrivacy.Private);
        var videoIds = Enumerable.Range(0, 45).Select(i => $"v{i}").ToList();

        var progress = new List<int>();
        var added = await _service.AddItemsAsync(playlistId, videoIds,
            (processed, total, _, _) => progress.Add(processed));

        Assert.Equal(45, added.Count);
        Assert.Equal(45, progress.Count);
        Assert.Equal(45, progress[^1]);
    }

    [Fact]
    public async Task AddItems_NoSetVideoId_MarkedFailed_NotFakedSuccess()
    {
        _service.BeginSession();
        var playlistId = await _service.CreatePlaylistAsync("L", "", PlaylistPrivacy.Private);
        _client.SuppressSetVideoIds = true;

        var added = await _service.AddItemsAsync(playlistId, ["v1"]);

        Assert.Empty(added); // not reported as added
        var addOp = _store.GetSessionOperations(_service.CurrentSessionId)
            .First(o => o.Type == OperationTypes.AddItem);
        Assert.Equal(OperationStatus.Failed, addOp.Status);
    }

    [Fact]
    public async Task DeletePlaylist_SnapshotsBeforeDeleting()
    {
        _service.BeginSession();
        var playlistId = await _service.CreatePlaylistAsync("L", "", PlaylistPrivacy.Private);
        await _service.AddItemsAsync(playlistId, ["v1", "v2"]);

        await _service.DeletePlaylistAsync(playlistId, "L", "", PlaylistPrivacy.Private);

        Assert.Contains(playlistId, _client.DeletedPlaylists);
        var deleteOp = _store.GetSessionOperations(_service.CurrentSessionId)
            .First(o => o.Type == OperationTypes.DeletePlaylist);
        Assert.Equal(OperationStatus.Done, deleteOp.Status);
        // Inverse references a snapshot that captured the 2 items.
        Assert.Contains("SnapshotId", deleteOp.InverseJson);
    }

    [Fact]
    public async Task EveryMutation_HasNonNullInverse_Invariant()
    {
        // §10 invariant, end-to-end: after a full create/add/remove/delete cycle,
        // no operation row may carry a null/empty inverse.
        _service.BeginSession();
        var playlistId = await _service.CreatePlaylistAsync("L", "", PlaylistPrivacy.Private);
        var added = await _service.AddItemsAsync(playlistId, ["v1", "v2", "v3"]);
        await _service.RemoveItemsAsync(playlistId,
            [new PlaylistItem(added[0].VideoId, added[0].SetVideoId!)]);
        await _service.DeletePlaylistAsync(playlistId, "L", "", PlaylistPrivacy.Private);

        foreach (var op in _store.GetSessionOperations(_service.CurrentSessionId))
        {
            Assert.False(string.IsNullOrWhiteSpace(op.InverseJson));
            Assert.NotEqual("null", op.InverseJson);
        }
    }
}
