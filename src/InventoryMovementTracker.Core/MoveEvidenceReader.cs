using Microsoft.Data.Sqlite;

namespace InventoryMovementTracker.Core;

public static class MoveEvidenceReader
{
    public static IReadOnlyList<MoveEvidence> Read(string path)
    {
        // Pooling off so the file is released on dispose; the old database is evidence, never written.
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, line_offset, item_ix, ts, move_type, item_class, item_geid, amount,
                   src_kind, src_key, tgt_kind, tgt_key, result, action, price
            FROM move
            ORDER BY ts, session_id, line_offset, item_ix
            """;
        using var r = cmd.ExecuteReader();
        var rows = new List<MoveEvidence>();
        while (r.Read())
            rows.Add(new MoveEvidence(
                new Provenance(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2)),
                r.GetString(3), r.GetString(4), r.GetString(5), Text(r, 6), r.GetInt64(7),
                Text(r, 8), Text(r, 9), Text(r, 10), Text(r, 11), Text(r, 12), Text(r, 13),
                r.IsDBNull(14) ? null : r.GetDouble(14)));
        return rows;
    }

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
