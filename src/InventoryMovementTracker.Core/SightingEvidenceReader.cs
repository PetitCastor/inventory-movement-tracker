using static InventoryMovementTracker.Core.EvidenceDb;

namespace InventoryMovementTracker.Core;

public static class SightingEvidenceReader
{
    public static IReadOnlyList<SightingEvidence> Read(string path) => Query(path, """
        SELECT session_id, line_offset, ts, geid, class_name, port, burst_id, parent_geid, placeholder
        FROM attachment_sighting
        ORDER BY ts, session_id, line_offset
        """, r => new SightingEvidence(
            new Provenance(r.GetInt64(0), r.GetInt64(1), 0),
            r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt64(6),
            Text(r, 7), r.GetInt64(8) != 0));
}
