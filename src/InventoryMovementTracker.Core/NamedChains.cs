namespace InventoryMovementTracker.Core;

/// <summary>
/// Turns the named evidence (moves with a geid, body sightings) into one <see cref="ObjectChain"/> per geid: P0.1 §3
/// and §4, reduced to mandate B4b. A geid's states are the holders its own evidence names, plus Out of sight, where
/// it starts. Out of scope: the lost-queue rule of §4 (B9), births (B5), continuous hazards (B6), resets (B7) and
/// moves by class (B9).
/// </summary>
public static class NamedChains
{
    private static readonly HashSet<string> NamedMoves = ["Store", "Interaction", "StoreItem", "Drop", "Death"];
    private static readonly HashSet<string> Sources = ["Container", "Location", "Equipped"];   // Invalid: no likelihood
    private static readonly HashSet<string> Targets = ["Container", "Location", "Equipped", "Corpse", "World"];

    /// <returns>Each geid's steps in evidence order: by ts, then moves before listings, then provenance.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Run(
        IEnumerable<MoveEvidence> moves, IEnumerable<SightingEvidence> sightings, double epsilon, double delta)
    {
        // A move that succeeded or got no answer weighs its source, then jumps to its target; a Drop's target is the
        // floor under its key as stored. failed: nothing. lost: the source only, no jump; the §4 queue rule waits for B9.
        var evidence = moves
            .Where(m => m.Geid is not null && NamedMoves.Contains(m.MoveType) && m.Result != "failed")
            .Select(m => (Geid: m.Geid!, m.Ts, Phase: 0, m.Provenance, Vanish: false,
                Source: Sources.Contains(m.SourceKind ?? "") ? Of(m.SourceKind!, m.SourceKey) : null,
                Target: m.Result != "lost" && Targets.Contains(m.TargetKind ?? "") ? Of(m.TargetKind!, m.TargetKey) : null))
            .ToList();

        // A seen line, placeholders aside, is named evidence: a jump to the body, or to its parent container.
        var seen = sightings.Where(s => !s.Placeholder).ToList();
        evidence.AddRange(seen.Select(s => (s.Geid, s.Ts, Phase: 1, s.Provenance, Vanish: false, Source: (Holder?)null,
            Target: (Holder?)(s.ParentGeid is { } parent ? new Holder("Container", parent) : Holder.Body))));

        var states = evidence.GroupBy(e => e.Geid).ToDictionary(g => g.Key, g => g
            .SelectMany(e => new[] { e.Source, e.Target }).OfType<Holder>().Prepend(Holder.OutOfSight).Distinct().ToList());

        // A burst with the Body_ItemPort line is a complete listing: a geid with the body among its states that the
        // listing leaves out goes from the body to Out of sight with 1 − δ.
        foreach (var burst in seen.GroupBy(s => (s.Provenance.SessionId, s.BurstId)))
        {
            if (burst.FirstOrDefault(s => s.Port == "Body_ItemPort") is not { } body) continue;
            var listed = burst.Select(s => s.Geid).ToHashSet();
            evidence.AddRange(states.Where(kv => kv.Value.Contains(Holder.Body) && !listed.Contains(kv.Key))
                .Select(kv => (kv.Key, body.Ts, 1, body.Provenance, true, (Holder?)null, (Holder?)null)));
        }

        var chains = new Dictionary<string, IReadOnlyList<ChainEntry>>();
        foreach (var geid in evidence
            .OrderBy(e => e.Ts, StringComparer.Ordinal).ThenBy(e => e.Phase)
            .ThenBy(e => e.Provenance.SessionId).ThenBy(e => e.Provenance.LineOffset).ThenBy(e => e.Provenance.ItemIx)
            .GroupBy(e => e.Geid))
        {
            var holders = states[geid.Key];
            var steps = geid.SkipWhile(e => e.Vanish).ToList();   // a listing before the first evidence says nothing
            int? At(Holder? h) => h is null ? null : holders.IndexOf(h);
            var (predictive, smoothed) = ObjectChain.Run([1, .. new double[holders.Count - 1]], [.. steps.Select(e => e.Vanish
                ? LeaveBody(holders.Count, holders.IndexOf(Holder.Body), delta)
                : ChainStep.Move(holders.Count, At(e.Source), At(e.Target), epsilon))]);

            IReadOnlyDictionary<Holder, double> Law(double[] p) => holders.Zip(p).ToDictionary(x => x.First, x => x.Second);
            chains[geid.Key] = [.. steps.Select((e, t) => new ChainEntry(e.Provenance, e.Ts, e.Source,
                t > 0 && e.Source?.Kind is "Container" or "Location" ? predictive[t][holders.IndexOf(e.Source!)] : null,
                Law(predictive[t]), Law(smoothed[t])))];
        }
        return chains;
    }

    private static Holder Of(string kind, string? key) => kind == "Equipped" ? Holder.Body : new Holder(kind, key ?? "");

    // Out of sight is state 0. Every other state stays where it is.
    private static ChainStep LeaveBody(int k, int body, double delta)
    {
        var m = new double[k, k];
        for (var i = 0; i < k; i++) m[i, i] = 1;
        (m[body, body], m[body, 0]) = (delta, 1 - delta);
        return new ChainStep(m, null);
    }
}
