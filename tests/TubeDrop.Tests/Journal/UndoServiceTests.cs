using TubeDrop.Core.Journal;
using TubeDrop.Core.Playlists;

namespace TubeDrop.Tests.Journal;

public sealed class UndoServiceTests : IDisposable
{
    private readonly string _dbDir;
    private readonly JournalStore _store;
    private readonly FakePlaylistClient _client = new();
    private readonly JournaledPlaylistService _service;
    private readonly UndoService _undo;

    public UndoServiceTests()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), "TubeDropTests", Guid.NewGuid().ToString("N"));
        _store = new JournalStore(Path.Combine(_dbDir, "journal.db"));
        _service = new JournaledPlaylistService(_store, _client);
        _undo = new UndoService(_store, _service);
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
    public async Task UndoAdd_RemovesItem()
    {
        _service.BeginSession();
        var playlistId = await _service.CreatePlaylistAsync("L", "", PlaylistPrivacy.Private);
        await _service.AddItemsAsync(playlistId, ["v1"]);
        var addOp = _store.GetSessionOperations(_service.CurrentSessionId)
            .First(o => o.Type == OperationTypes.AddItem);

        var outcome = await _undo.UndoOperationAsync(addOp.Id);

        Assert.True(outcome.Succeeded);
        Assert.Empty(_client.Playlists[playlistId]);
        Assert.Equal(OperationStatus.Undone, _store.GetOperation(addOp.Id)!.Status);
    }

    [Fact]
    public async Task UndoDelete_RebuildsFromSnapshot_NewId()
    {
        _service.BeginSession();
        var playlistId = await _service.CreatePlaylistAsync("My List", "desc", PlaylistPrivacy.Private);
        await _service.AddItemsAsync(playlistId, ["v1", "v2"]);
        await _service.DeletePlaylistAsync(playlistId, "My List", "desc", PlaylistPrivacy.Private);
        var deleteOp = _store.GetSessionOperations(_service.CurrentSessionId)
            .First(o => o.Type == OperationTypes.DeletePlaylist);

        var outcome = await _undo.UndoOperationAsync(deleteOp.Id);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.NewPlaylistId);
        Assert.NotEqual(playlistId, outcome.NewPlaylistId); // YouTube has no trash
        Assert.Equal(2, _client.Playlists[outcome.NewPlaylistId!].Count);
    }

    [Fact]
    public async Task UndoCreate_DeletesPlaylist()
    {
        _service.BeginSession();
        var playlistId = await _service.CreatePlaylistAsync("L", "", PlaylistPrivacy.Private);
        var createOp = _store.GetSessionOperations(_service.CurrentSessionId)
            .First(o => o.Type == OperationTypes.CreatePlaylist);

        var outcome = await _undo.UndoOperationAsync(createOp.Id);

        Assert.True(outcome.Succeeded);
        Assert.Contains(playlistId, _client.DeletedPlaylists);
    }

    [Fact]
    public async Task Undo_IsItselfJournaled_EnablesRedo()
    {
        _service.BeginSession();
        var playlistId = await _service.CreatePlaylistAsync("L", "", PlaylistPrivacy.Private);
        await _service.AddItemsAsync(playlistId, ["v1"]);
        var addOp = _store.GetSessionOperations(_service.CurrentSessionId)
            .First(o => o.Type == OperationTypes.AddItem);

        await _undo.UndoOperationAsync(addOp.Id);

        // The undo (a remove) is itself recorded as a Done remove_item op with an
        // add inverse → redo is undoing that.
        var removeOp = _store.GetSessionOperations(_service.CurrentSessionId)
            .First(o => o.Type == OperationTypes.RemoveItem);
        Assert.Equal(OperationStatus.Done, removeOp.Status);

        var redo = await _undo.UndoOperationAsync(removeOp.Id);
        Assert.True(redo.Succeeded);
        Assert.Single(_client.Playlists[playlistId]); // v1 back
    }

    [Fact]
    public async Task UndoSession_UndoesAllNewestFirst()
    {
        _service.BeginSession();
        var playlistId = await _service.CreatePlaylistAsync("L", "", PlaylistPrivacy.Private);
        await _service.AddItemsAsync(playlistId, ["v1", "v2"]);

        var results = await _undo.UndoSessionAsync(_service.CurrentSessionId);

        Assert.All(results, r => Assert.True(r.Outcome.Succeeded));
        // create + 2 adds all undone
        Assert.Contains(playlistId, _client.DeletedPlaylists);
    }

    [Fact]
    public async Task UndoAlreadyUndone_Fails()
    {
        _service.BeginSession();
        var playlistId = await _service.CreatePlaylistAsync("L", "", PlaylistPrivacy.Private);
        await _service.AddItemsAsync(playlistId, ["v1"]);
        var addOp = _store.GetSessionOperations(_service.CurrentSessionId)
            .First(o => o.Type == OperationTypes.AddItem);
        await _undo.UndoOperationAsync(addOp.Id);

        var second = await _undo.UndoOperationAsync(addOp.Id);

        Assert.False(second.Succeeded);
    }
}
