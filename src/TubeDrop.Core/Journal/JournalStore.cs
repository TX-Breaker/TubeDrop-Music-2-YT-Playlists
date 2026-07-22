using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TubeDrop.Core.Journal;

public enum OperationStatus
{
    Pending,
    Done,
    Failed,
    Undone,
}

public sealed record JournalOperation(
    long Id,
    long SessionId,
    DateTimeOffset Timestamp,
    string Type,
    string PayloadJson,
    string InverseJson,
    OperationStatus Status);

public sealed record PlaylistSnapshot(
    long Id,
    string PlaylistId,
    DateTimeOffset Timestamp,
    string Title,
    string Description,
    string Privacy,
    string ItemsJson);

/// <summary>
/// SQLite-backed operation journal (§10): sessions, operations with their
/// inverses, playlist snapshots. Journal-before-execute is enforced by
/// <see cref="JournaledPlaylistService"/>; this class is pure storage.
/// </summary>
public sealed class JournalStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public JournalStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
        Execute("""
            CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                started_ts TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS operations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL REFERENCES sessions(id),
                ts TEXT NOT NULL,
                type TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                inverse_json TEXT NOT NULL,
                status TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS playlist_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                playlist_id TEXT NOT NULL,
                ts TEXT NOT NULL,
                title TEXT NOT NULL,
                description TEXT NOT NULL,
                privacy TEXT NOT NULL,
                items_json TEXT NOT NULL
            );
            """);
    }

    public long BeginSession()
    {
        Execute("INSERT INTO sessions (started_ts) VALUES ($ts)",
            ("$ts", DateTimeOffset.UtcNow.ToString("O")));
        return LastInsertRowId();
    }

    /// <summary>Records a mutating operation together with its inverse. Fails loudly on a missing inverse — the §10 invariant.</summary>
    public long RecordOperation(long sessionId, string type, object payload, object inverse)
    {
        var inverseJson = JsonSerializer.Serialize(inverse);
        if (string.IsNullOrWhiteSpace(inverseJson) || inverseJson == "null")
        {
            throw new InvalidOperationException($"Operation '{type}' has no valid inverse — refusing to record");
        }

        Execute("""
            INSERT INTO operations (session_id, ts, type, payload_json, inverse_json, status)
            VALUES ($session, $ts, $type, $payload, $inverse, $status)
            """,
            ("$session", sessionId),
            ("$ts", DateTimeOffset.UtcNow.ToString("O")),
            ("$type", type),
            ("$payload", JsonSerializer.Serialize(payload)),
            ("$inverse", inverseJson),
            ("$status", OperationStatus.Pending.ToString()));
        return LastInsertRowId();
    }

    public void SetStatus(long operationId, OperationStatus status) =>
        Execute("UPDATE operations SET status = $status WHERE id = $id",
            ("$status", status.ToString()), ("$id", operationId));

    /// <summary>Replaces the payload after execution (e.g. to store the setVideoId YouTube returned).</summary>
    public void UpdatePayloadAndInverse(long operationId, object payload, object inverse)
    {
        var inverseJson = JsonSerializer.Serialize(inverse);
        if (string.IsNullOrWhiteSpace(inverseJson) || inverseJson == "null")
        {
            throw new InvalidOperationException("Refusing to overwrite an inverse with null");
        }

        Execute("UPDATE operations SET payload_json = $payload, inverse_json = $inverse WHERE id = $id",
            ("$payload", JsonSerializer.Serialize(payload)),
            ("$inverse", inverseJson),
            ("$id", operationId));
    }

    public JournalOperation? GetOperation(long id) =>
        Query("SELECT id, session_id, ts, type, payload_json, inverse_json, status FROM operations WHERE id = $id",
            ("$id", id)).FirstOrDefault();

    public IReadOnlyList<JournalOperation> GetSessionOperations(long sessionId) =>
        Query("SELECT id, session_id, ts, type, payload_json, inverse_json, status FROM operations WHERE session_id = $s ORDER BY id",
            ("$s", sessionId));

    public IReadOnlyList<(long SessionId, DateTimeOffset StartedAt, int OperationCount)> GetSessions()
    {
        var result = new List<(long, DateTimeOffset, int)>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.started_ts, COUNT(o.id)
            FROM sessions s LEFT JOIN operations o ON o.session_id = s.id
            GROUP BY s.id ORDER BY s.id DESC
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add((reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetInt32(2)));
        }

        return result;
    }

    public long SaveSnapshot(string playlistId, string title, string description, string privacy, string itemsJson)
    {
        Execute("""
            INSERT INTO playlist_snapshots (playlist_id, ts, title, description, privacy, items_json)
            VALUES ($pid, $ts, $title, $desc, $privacy, $items)
            """,
            ("$pid", playlistId),
            ("$ts", DateTimeOffset.UtcNow.ToString("O")),
            ("$title", title),
            ("$desc", description),
            ("$privacy", privacy),
            ("$items", itemsJson));
        return LastInsertRowId();
    }

    public PlaylistSnapshot? GetSnapshot(long id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, playlist_id, ts, title, description, privacy, items_json FROM playlist_snapshots WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new PlaylistSnapshot(reader.GetInt64(0), reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6))
            : null;
    }

    public void Dispose() => _connection.Dispose();

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        cmd.ExecuteNonQuery();
    }

    private List<JournalOperation> Query(string sql, params (string Name, object Value)[] parameters)
    {
        var result = new List<JournalOperation>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new JournalOperation(
                reader.GetInt64(0),
                reader.GetInt64(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                Enum.Parse<OperationStatus>(reader.GetString(6))));
        }

        return result;
    }

    private long LastInsertRowId()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)cmd.ExecuteScalar()!;
    }
}
