using Microsoft.Data.Sqlite;
using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class SessionEvidenceReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"session-{Guid.NewGuid():N}.db");

    // The session table's DDL as the old app creates it (TrackerDb.cs), comments dropped.
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS session (
            id           INTEGER PRIMARY KEY,
            path         TEXT    NOT NULL UNIQUE,
            first_ts     TEXT,
            last_ts      TEXT,
            byte_offset  INTEGER NOT NULL DEFAULT 0,
            complete     INTEGER NOT NULL DEFAULT 0,
            unrecognised INTEGER NOT NULL DEFAULT 0,
            last_line_ts TEXT,
            cur_loc_id    TEXT,
            cur_loc_since TEXT,
            cur_landing_id TEXT,
            build         INTEGER,
            first_loc_id    TEXT,
            first_loc_since TEXT,
            char_reset_at   TEXT
        );
        INSERT INTO session(id, path, first_ts, last_ts, build, first_loc_id, first_loc_since, char_reset_at)
            VALUES (3, 'C:\Users\someone\Game.log', '2026-11-03T10:00:00Z', '2026-11-03T12:00:00Z', 9876543, 'loc', '2026-11-03T10:01:00Z', '2026-11-03T09:59:00Z');
        INSERT INTO session(id, path) VALUES (1, 'a.log');
        """;

    public SessionEvidenceReaderTests()
    {
        using var conn = new SqliteConnection($"Data Source={_path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Ddl;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Read_returns_every_row_ordered_by_id_keeping_nulls()
    {
        var rows = SessionEvidenceReader.Read(_path);

        Assert.Equal(
            [
                new SessionEvidence(1, null, null, null, null, null, null),
                new SessionEvidence(3, "2026-11-03T10:00:00Z", "2026-11-03T12:00:00Z", 9876543, "loc", "2026-11-03T10:01:00Z", "2026-11-03T09:59:00Z"),
            ],
            rows);
    }
}
