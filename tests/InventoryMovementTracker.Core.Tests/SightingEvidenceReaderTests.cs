using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class SightingEvidenceReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"sighting-{Guid.NewGuid():N}.db");

    // attachment_sighting's DDL as the old app creates it (TrackerDb.cs), plus a bare session table for its foreign key.
    private const string Ddl = """
        CREATE TABLE session (id INTEGER PRIMARY KEY);
        CREATE TABLE IF NOT EXISTS attachment_sighting (
            session_id  INTEGER NOT NULL REFERENCES session(id) ON DELETE CASCADE,
            line_offset INTEGER NOT NULL,
            ts          TEXT    NOT NULL,
            geid        TEXT    NOT NULL,
            class_name  TEXT    NOT NULL,
            port        TEXT    NOT NULL,
            burst_id    INTEGER NOT NULL,
            parent_geid TEXT,
            placeholder INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (session_id, line_offset)
        );
        INSERT INTO session(id) VALUES (1), (2);
        INSERT INTO attachment_sighting VALUES (2, 50, '2026-11-02T10:00:00.1234567Z', '9', 'rifle', 'hand', 7, 'p1', 1);
        INSERT INTO attachment_sighting VALUES (1, 900, '2026-11-01T10:00:00.0000000Z', '1', 'helmet', 'head', 3, NULL, 0);
        INSERT INTO attachment_sighting VALUES (2, 20, '2026-10-31T10:00:00.0000000Z', '2', 'boots', 'feet', 3, NULL, 0);
        INSERT INTO attachment_sighting VALUES (1, 100, '2026-11-01T10:00:00.0000000Z', '3', 'visor', 'face', 3, NULL, 0);
        """;

    public SightingEvidenceReaderTests()
    {
        using var conn = Open();
        Exec(conn, Ddl);
    }

    public void Dispose()
    {
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" }) File.Delete(f);
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_path};Pooling=False");
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Read_returns_every_row_ordered_with_nulls_and_placeholder()
    {
        var rows = SightingEvidenceReader.Read(_path);

        Assert.Equal(
            [new Provenance(2, 20, 0), new Provenance(1, 100, 0), new Provenance(1, 900, 0), new Provenance(2, 50, 0)],
            rows.Select(r => r.Provenance));
        Assert.Null(rows[0].ParentGeid);
        Assert.False(rows[0].Placeholder);
        Assert.Equal(
            new SightingEvidence(new Provenance(2, 50, 0), "2026-11-02T10:00:00.1234567Z", "9", "rifle", "hand", 7, "p1", true),
            rows[3]);
    }

    [Fact]
    public void Read_a_wal_database_leaves_the_main_file_byte_identical()
    {
        using var writer = Open();
        Exec(writer, "PRAGMA journal_mode=WAL;");
        // A committed row still sitting in the WAL: the reader must see it without rewriting the main file.
        Exec(writer, "INSERT INTO attachment_sighting VALUES (1, 5, '2026-10-01T00:00:00.0000000Z', '4', 'cap', 'head', 1, NULL, 0);");
        var before = HashMainFile();

        var rows = SightingEvidenceReader.Read(_path);

        Assert.Equal(5, rows.Count);
        Assert.Equal(new Provenance(1, 5, 0), rows[0].Provenance);
        Assert.Equal(before, HashMainFile());
    }

    // The writer connection stays open, so the file must be opened with sharing that tolerates it.
    private byte[] HashMainFile()
    {
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(fs);
    }
}
