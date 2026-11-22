using Microsoft.Data.Sqlite;

namespace InventoryMovementTracker.Core;

public static class MoveEvidenceReader
{
    public static IReadOnlyList<MoveEvidence> Read(string path)
    {
        // Pooling off so the file is released on dispose; the old database is evidence, never written.
        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
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
                r.GetString(3), r.GetString(4), r.GetString(5), r.GetValue(6) as string, r.GetInt64(7),
                r.GetValue(8) as string, r.GetValue(9) as string, r.GetValue(10) as string, r.GetValue(11) as string,
                r.GetValue(12) as string, r.GetValue(13) as string,
                r.IsDBNull(14) ? null : r.GetDouble(14)));
        return rows;
    }
}
