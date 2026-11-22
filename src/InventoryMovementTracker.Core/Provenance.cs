namespace InventoryMovementTracker.Core;

/// <summary>Where a move came from: the log line's byte offset within its session, and the position in a multi-select drag.</summary>
public sealed record Provenance(long SessionId, long LineOffset, long ItemIx);
