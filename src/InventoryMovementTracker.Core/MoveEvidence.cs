namespace InventoryMovementTracker.Core;

/// <summary>One row of the old app's move table, columns kept as stored: no enums, no interpretation.</summary>
public sealed record MoveEvidence(
    Provenance Provenance,
    string Ts,
    string MoveType,
    string ItemClass,
    string? Geid,
    long Amount,
    string? SourceKind,
    string? SourceKey,
    string? TargetKind,
    string? TargetKey,
    string? Result,
    string? Action,
    double? Price);
