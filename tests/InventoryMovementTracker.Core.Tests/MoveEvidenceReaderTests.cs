using Microsoft.Data.Sqlite;
using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class MoveEvidenceReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"move-{Guid.NewGuid():N}.db");

    // The move table's DDL as the old app creates it (TrackerDb.cs), plus a bare session table for its foreign key.
    private const string Ddl = """
        CREATE TABLE session (id INTEGER PRIMARY KEY);
        CREATE TABLE IF NOT EXISTS move (
            id          INTEGER PRIMARY KEY,
            session_id  INTEGER NOT NULL REFERENCES session(id) ON DELETE CASCADE,
            line_offset INTEGER NOT NULL,
            request_no  INTEGER NOT NULL,
            ts          TEXT    NOT NULL,
            player      TEXT,
            player_id   TEXT,
            move_type   TEXT    NOT NULL,
            item_class  TEXT    NOT NULL,
            item_geid   TEXT,
            amount      INTEGER NOT NULL DEFAULT 1,
            item_ix     INTEGER NOT NULL DEFAULT 0,
            src_raw     TEXT,
            src_kind    TEXT,
            src_key     TEXT,
            tgt_raw     TEXT,
            tgt_kind    TEXT,
            tgt_key     TEXT,
            caller      TEXT,
            action      TEXT,
            result      TEXT,
            price       REAL,
            UNIQUE(session_id, line_offset, item_ix)
        );
        INSERT INTO session(id) VALUES (1), (2);
        INSERT INTO move(session_id, line_offset, request_no, ts, move_type, item_class, item_geid, amount, item_ix, src_kind, src_key, tgt_kind, tgt_key, action, result, price)
            VALUES (2, 50, 1, '2026-11-02T10:00:00.1234567Z', 'Store', 'rifle', '123', 1, 0, 'Inventory', 'a', 'Inventory', 'b', 'Carry', 'ok', 12.5);
        INSERT INTO move(session_id, line_offset, request_no, ts, move_type, item_class, amount, item_ix)
            VALUES (1, 900, 2, '2026-11-01T10:00:00.0000000Z', 'Move', 'medpen', 3, 1);
        INSERT INTO move(session_id, line_offset, request_no, ts, move_type, item_class, amount, item_ix)
            VALUES (1, 900, 2, '2026-11-01T10:00:00.0000000Z', 'Move', 'helmet', 1, 0);
        INSERT INTO move(session_id, line_offset, request_no, ts, move_type, item_class)
            VALUES (1, 700, 3, '2026-11-03T10:00:00.0000000Z', 'Move', 'late');
        INSERT INTO move(session_id, line_offset, request_no, ts, move_type, item_class)
            VALUES (2, 20, 4, '2026-11-01T11:00:00.0000000Z', 'Move', 'tie-b');
        INSERT INTO move(session_id, line_offset, request_no, ts, move_type, item_class)
            VALUES (2, 10, 4, '2026-11-01T11:00:00.0000000Z', 'Move', 'tie-a');
        """;

    public MoveEvidenceReaderTests()
    {
        using var conn = new SqliteConnection($"Data Source={_path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Ddl;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Read_returns_every_row_ordered_with_provenance_and_nulls()
    {
        var rows = MoveEvidenceReader.Read(_path);

        Assert.Equal(
            [
                new Provenance(1, 900, 0), new Provenance(1, 900, 1),
                new Provenance(2, 10, 0), new Provenance(2, 20, 0),
                new Provenance(2, 50, 0), new Provenance(1, 700, 0),
            ],
            rows.Select(r => r.Provenance));
        Assert.Null(rows[0].Geid);
        Assert.Null(rows[0].Price);
        Assert.Null(rows[0].SourceKind);
        Assert.Equal(3, rows[1].Amount);
        Assert.Equal("123", rows[4].Geid);
        Assert.Equal("b", rows[4].TargetKey);
        Assert.Equal("Carry", rows[4].Action);
        Assert.Equal(12.5, rows[4].Price);
    }

    [Fact]
    public void Read_refuses_to_create_a_missing_database()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.db");

        Assert.Throws<SqliteException>(() => MoveEvidenceReader.Read(missing));
        Assert.False(File.Exists(missing));
    }
}
