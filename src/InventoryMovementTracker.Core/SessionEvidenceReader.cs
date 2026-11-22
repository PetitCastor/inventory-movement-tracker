using static InventoryMovementTracker.Core.EvidenceDb;

namespace InventoryMovementTracker.Core;

public static class SessionEvidenceReader
{
    public static IReadOnlyList<SessionEvidence> Read(string path) => Query(path, """
        SELECT id, first_ts, last_ts, build, first_loc_id, first_loc_since, char_reset_at
        FROM session
        ORDER BY id
        """, r => new SessionEvidence(
            r.GetInt64(0), Text(r, 1), Text(r, 2), r.IsDBNull(3) ? null : r.GetInt64(3),
            Text(r, 4), Text(r, 5), Text(r, 6)));
}
