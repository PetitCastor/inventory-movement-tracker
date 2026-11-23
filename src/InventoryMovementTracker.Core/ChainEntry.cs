namespace InventoryMovementTracker.Core;

/// <summary>
/// One step of a geid's chain. Source is the move's source when the step weighs it. SourceProbability is
/// P(X = Source | evidence strictly before), set only for a check (P0.1 §9): a move from a real source
/// (Container or Location) for a geid that already had evidence. Predictive is the law just before the step,
/// Smoothed the law after it given all the evidence. Evidence: the lines that carry Predictive, sorted, without this
/// step's own (E5), computed at each enumeration. It is sufficient, not minimal: a line of no effect may be in it. A lost
/// queue brings all its lines, even later ones, and the one that closes it. Resets and game updates, session lines without
/// a provenance, are not in it, and the lots publish no list. The law after the last step has that step's Evidence plus its
/// Provenance. Known limits: a complete listing brings its Body_ItemPort line only, not its burst, so the list may be
/// insufficient when a move, by class or named of a listed geid, falls between that line and the sight of a listed geid of
/// its burst, equal Ts included (R34, R38); and floating-point noise, 1e-16: a residue of mass may make a step that the
/// replay of the list lacks (R39), and an exact tie in the hard decision whether a container is carried, settled by that
/// noise, may be settled the other way in the replay (R47). No threshold is set, since it would change the laws.
/// </summary>
public sealed record ChainEntry(
    Provenance Provenance,
    string Ts,
    Holder? Source,
    double? SourceProbability,
    IReadOnlyDictionary<Holder, double> Predictive,
    IReadOnlyDictionary<Holder, double> Smoothed,
    IEnumerable<Provenance> Evidence);
