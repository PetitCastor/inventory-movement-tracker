namespace InventoryMovementTracker.Core;

/// <summary>One row of the old app's attachment_sighting table, columns kept as stored. ItemIx is always 0.</summary>
public sealed record SightingEvidence(
    Provenance Provenance,
    string Ts,
    string Geid,
    string ClassName,
    string Port,
    long BurstId,
    string? ParentGeid,
    bool Placeholder);
