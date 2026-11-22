namespace InventoryMovementTracker.Core;

/// <summary>
/// One step of a geid's chain. Source is the move's source when the step weighs it. SourceProbability is
/// P(X = Source | evidence strictly before), set only for a check (P0.1 §9): a move from a real source
/// (Container or Location) for a geid that already had evidence. Predictive is the law just before the step,
/// Smoothed the law after it given all the evidence.
/// </summary>
public sealed record ChainEntry(
    Provenance Provenance,
    string Ts,
    Holder? Source,
    double? SourceProbability,
    IReadOnlyDictionary<Holder, double> Predictive,
    IReadOnlyDictionary<Holder, double> Smoothed);
