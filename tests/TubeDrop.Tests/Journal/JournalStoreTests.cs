using TubeDrop.Core.Journal;

namespace TubeDrop.Tests.Journal;

public sealed class JournalStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly JournalStore _store;

    public JournalStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "TubeDropTests", Guid.NewGuid().ToString("N"), "journal.db");
        _store = new JournalStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            Directory.Delete(Path.GetDirectoryName(_dbPath)!, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void RecordOperation_RoundTrips()
    {
        var sessionId = _store.BeginSession();

        var opId = _store.RecordOperation(sessionId, OperationTypes.AddItem,
            new AddItemPayload("PL1", "vid", null),
            new RemoveItemPayload("PL1", "SET1", "vid"));

        var op = _store.GetOperation(opId);
        Assert.NotNull(op);
        Assert.Equal(OperationTypes.AddItem, op.Type);
        Assert.Equal(OperationStatus.Pending, op.Status);
        Assert.Contains("vid", op.PayloadJson);
    }

    [Fact]
    public void RecordOperation_NullInverse_Throws()
    {
        var sessionId = _store.BeginSession();

        // The §10 invariant: no mutating operation without a valid inverse.
        Assert.Throws<InvalidOperationException>(() =>
            _store.RecordOperation(sessionId, OperationTypes.AddItem,
                new AddItemPayload("PL1", "vid", null),
                null!));
    }

    [Fact]
    public void SetStatus_Persists()
    {
        var sessionId = _store.BeginSession();
        var opId = _store.RecordOperation(sessionId, OperationTypes.AddItem,
            new AddItemPayload("PL1", "vid", null), new RemoveItemPayload("PL1", "SET1", "vid"));

        _store.SetStatus(opId, OperationStatus.Done);

        Assert.Equal(OperationStatus.Done, _store.GetOperation(opId)!.Status);
    }

    [Fact]
    public void Snapshot_RoundTrips()
    {
        var snapId = _store.SaveSnapshot("PL1", "My List", "desc", "Private", "[]");

        var snap = _store.GetSnapshot(snapId);
        Assert.NotNull(snap);
        Assert.Equal("My List", snap.Title);
        Assert.Equal("PL1", snap.PlaylistId);
    }

    [Fact]
    public void GetSessions_CountsOperations()
    {
        var sessionId = _store.BeginSession();
        _store.RecordOperation(sessionId, OperationTypes.AddItem,
            new AddItemPayload("PL1", "v1", null), new RemoveItemPayload("PL1", "S1", "v1"));
        _store.RecordOperation(sessionId, OperationTypes.AddItem,
            new AddItemPayload("PL1", "v2", null), new RemoveItemPayload("PL1", "S2", "v2"));

        var sessions = _store.GetSessions();
        Assert.Single(sessions);
        Assert.Equal(2, sessions[0].OperationCount);
    }

    [Fact]
    public void Data_SurvivesReopen()
    {
        var sessionId = _store.BeginSession();
        var opId = _store.RecordOperation(sessionId, OperationTypes.CreatePlaylist,
            new CreatePlaylistPayload("PL1", "t", "d", "Private"),
            new DeletePlaylistPayload("PL1", -1));
        _store.SetStatus(opId, OperationStatus.Done);
        _store.Dispose();

        using var reopened = new JournalStore(_dbPath);
        Assert.Equal(OperationStatus.Done, reopened.GetOperation(opId)!.Status);
    }
}
