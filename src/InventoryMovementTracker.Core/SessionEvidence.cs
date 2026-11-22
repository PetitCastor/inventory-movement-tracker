namespace InventoryMovementTracker.Core;

/// <summary>One row of the old app's session table, columns kept as stored. The local log path is deliberately left out.</summary>
public sealed record SessionEvidence(
    long Id,
    string? FirstTs,
    string? LastTs,
    long? Build,
    string? FirstLocId,
    string? FirstLocSince,
    string? CharResetAt);
