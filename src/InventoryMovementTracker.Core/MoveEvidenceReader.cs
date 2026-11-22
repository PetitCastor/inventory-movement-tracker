using static InventoryMovementTracker.Core.EvidenceDb;

namespace InventoryMovementTracker.Core;

public static class MoveEvidenceReader
{
    public static IReadOnlyList<MoveEvidence> Read(string path) => Query(path, """
        SELECT session_id, line_offset, item_ix, ts, move_type, item_class, item_geid, amount,
               src_kind, src_key, tgt_kind, tgt_key, result, action, price
        FROM move
        ORDER BY ts, session_id, line_offset, item_ix
        """, r => new MoveEvidence(
            new Provenance(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2)),
            r.GetString(3), r.GetString(4), r.GetString(5), Text(r, 6), r.GetInt64(7),
            Text(r, 8), Text(r, 9), Text(r, 10), Text(r, 11), Text(r, 12), Text(r, 13),
            r.IsDBNull(14) ? null : r.GetDouble(14)));
}
