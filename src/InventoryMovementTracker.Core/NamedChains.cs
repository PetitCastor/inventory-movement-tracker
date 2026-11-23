using System.Globalization;

namespace InventoryMovementTracker.Core;

/// <summary>
/// Turns the evidence into one <see cref="ObjectChain"/> per geid: P0.1 §3, §4 and §6, reduced to mandates B4b and
/// B9b. A geid's states are the holders its own evidence names, plus Out of sight, where it starts, plus the targets
/// of the class moves it may have followed.
/// <para>
/// A move by class (no geid) is split with <see cref="ClassMoveSplit"/> among the named geids of its class that may
/// be at its source, so it needs every geid's current filter at once: one online forward pass runs over all the
/// evidence in time order and fixes each class move's kernels there. Each chain is then smoothed with those kernels as
/// they are. This frozen coupling is the approximation of decision B1, constraint 3: what a later step teaches about
/// one geid does not revise how an earlier class move was shared with the others.
/// </para>
/// <para>
/// Arrival order at the source, most recent first, is approximated by the time of each geid's last step that targeted
/// that source. The units the named geids do not supply (lots, carriers, Out of sight, births) are out of scope (B5);
/// only their expected count is returned. Also out of scope: the lost-queue rule of §4 and MoveAll (B9), births (B5)
/// and resets (B7). Continuous hazards (B6) and the game updates without a reset (B8, <c>updates</c>) apply only when
/// <c>hazards</c> is given.
/// </para>
/// </summary>
public static class NamedChains
{
    private static readonly HashSet<string> NamedMoves = ["Store", "Interaction", "StoreItem", "Drop", "Death"];
    private static readonly HashSet<string> NotClassMoves = ["Split", "Stack", "StackAll", "Sort", "AmmoRepool", "Buy", "MoveAll"];
    private static readonly string[] Consumables = ["Drink_", "Food_", "crlf_consumable_"];
    private static readonly HashSet<string> Sources = ["Container", "Location", "Equipped"];   // Invalid: no likelihood
    private static readonly HashSet<string> Targets = ["Container", "Location", "Equipped", "Corpse", "World"];

    /// <returns>
    /// Lots: the expected units per (holder, class) that no geid names, at the end. A lot is a mean-field count (an
    /// expected number, not a law): a successful Buy adds its Amount; a class move takes its expected shortfall from the
    /// source lot, at most what it holds, into the target lot; a geid first seen on a named move out of a lot of
    /// size L is that lot's unit with probability min(1, L), which then loses it. Chains: each geid's steps in evidence order (by ts, then moves before listings, then provenance), class moves
    /// included. MeanShortfall: the expected number of units per class move that no named geid supplied.
    /// </returns>
    public static (IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Chains, double MeanShortfall, IReadOnlyDictionary<(Holder, string), double> Lots) Run(
        IEnumerable<MoveEvidence> moves, IEnumerable<SightingEvidence> sightings, double epsilon, double delta, Hazards? hazards = null,
        IReadOnlyList<DateTimeOffset>? updates = null)
    {
        // A named move that succeeded or got no answer weighs its source, then jumps to its target; a Drop's target is
        // the floor under its key as stored. failed: nothing. lost: the source only, no jump; the §4 queue rule waits.
        // A seen line, placeholders aside, is named evidence: a jump to the body, or to its parent container.
        var moveList = moves.ToList();
        var seen = sightings.Where(s => !s.Placeholder).ToList();
        var named = moveList
            .Where(m => m.Geid is not null && NamedMoves.Contains(m.MoveType) && m.Result != "failed")
            .Select(m => (Geid: m.Geid!, Class: m.ItemClass, m.Ts, Phase: 0, m.Provenance, Source: SourceOf(m),
                Target: m.Result != "lost" ? TargetOf(m) : null))
            .Concat(seen.Select(s => (s.Geid, Class: s.ClassName, s.Ts, Phase: 1, s.Provenance, Source: (Holder?)null,
                Target: (Holder?)(s.ParentGeid is { } parent ? new Holder("Container", parent) : Holder.Body))))
            .ToList();

        var holders = named.GroupBy(e => e.Geid).ToDictionary(g => g.Key, g => g
            .SelectMany(e => new[] { e.Source, e.Target }).OfType<Holder>().Prepend(Holder.OutOfSight).Distinct().ToList());
        var byClass = named.GroupBy(e => e.Geid).ToLookup(g => g.First().Class, g => g.Key);
        var law = holders.ToDictionary(kv => kv.Key, kv => (double[])[1, .. new double[kv.Value.Count - 1]]);
        var steps = holders.ToDictionary(kv => kv.Key, _ => new List<(Provenance P, string Ts, Holder? Source, Func<List<Holder>, ChainStep> Kernel, Func<List<Holder>, ChainStep>? Decay)>());
        var classOf = named.GroupBy(e => e.Geid).ToDictionary(g => g.Key, g => g.First().Class);
        var lastTs = new Dictionary<string, DateTimeOffset>();
        var pending = new Dictionary<string, Func<List<Holder>, ChainStep>>();
        var arrived = new Dictionary<(string, Holder), int>();
        var shortfalls = new List<double>();
        var lots = new Dictionary<(Holder, string), double>();
        var births = new Dictionary<string, (Holder Source, double Q)>();
        double[] Prior(string geid, int k)
        {
            var p = new double[k];
            p[0] = 1;
            if (births.TryGetValue(geid, out var b)) { p[0] = 1 - b.Q; p[holders[geid].IndexOf(b.Source)] = b.Q; }
            return p;
        }

        // B6: before a geid's step, D decays the law over the hours since its previous step (none before the first).
        // It is stored apart so that the published predictive law is the one after D, just before the proof.
        void Decay(string geid, string ts)
        {
            if (hazards is null || pending.ContainsKey(geid)) return;
            var now = DateTimeOffset.Parse(ts, CultureInfo.InvariantCulture);
            var first = !lastTs.TryGetValue(geid, out var last);
            var hours = first ? 0 : Math.Max(0, (now - last).TotalHours);
            var u = first || updates is null ? 0 : updates.Count(x => last < x && x <= now);   // B8: ]previous step, now]
            lastTs[geid] = now;
            pending[geid] = h => DecayStep(h, hazards, classOf[geid], hours, u);
            law[geid] = ObjectChain.Filter(law[geid], pending[geid](holders[geid]), $"at {ts}");
        }

        void Apply(string geid, int seq, Provenance p, string ts, Holder? source, Holder? target, Func<List<Holder>, ChainStep> kernel)
        {
            Decay(geid, ts);
            if (target is not null && !holders[geid].Contains(target)) { holders[geid].Add(target); law[geid] = [.. law[geid], 0]; }
            law[geid] = ObjectChain.Filter(law[geid], kernel(holders[geid]), $"at {ts}");
            steps[geid].Add((p, ts, source, kernel, pending.Remove(geid, out var decay) ? decay : null));
            if (target is not null) arrived[(geid, target)] = seq;
        }

        var events = named.Select(e => (e.Ts, e.Phase, e.Provenance, Run: (Action<int>)(seq =>
        {
            if (!steps[e.Geid].Any() && e.Source is { } s0 && lots.GetValueOrDefault((s0, e.Class)) is var lot and > 0)
            {
                var q = Math.Min(1, lot);
                births[e.Geid] = (s0, q);
                lots[(s0, e.Class)] = lot - q;
                law[e.Geid] = Prior(e.Geid, law[e.Geid].Length);
            }
            Apply(e.Geid, seq, e.Provenance, e.Ts, e.Source, e.Target, h => ChainStep.Move(h.Count, At(h, e.Source), At(h, e.Target), epsilon));
        }))).ToList();

        // A successful Buy: its Amount is born in the target, which already is the landing station.
        foreach (var m in moveList.Where(m => m.MoveType == "Buy" && m.Result == "succeed" && m.Amount > 0))
            if (TargetOf(m) is { } at)
                events.Add((m.Ts, 0, m.Provenance, _ => lots[(at, m.ItemClass)] = lots.GetValueOrDefault((at, m.ItemClass)) + m.Amount));

        // A burst with the Body_ItemPort line is a complete listing: a geid that may be on the body and that the listing
        // leaves out goes from the body to Out of sight with 1 − δ.
        foreach (var burst in seen.GroupBy(s => (s.Provenance.SessionId, s.BurstId)))
        {
            if (burst.FirstOrDefault(s => s.Port == "Body_ItemPort") is not { } body) continue;
            var listed = burst.Select(s => s.Geid).ToHashSet();
            events.Add((body.Ts, 1, body.Provenance, seq =>
            {
                foreach (var geid in law.Keys.Where(g => !listed.Contains(g) && At(holders[g], Holder.Body) is { } b && law[g][b] > 0).ToList())
                    Apply(geid, seq, body.Provenance, body.Ts, null, null, h => Shift(h, Holder.Body, Holder.OutOfSight, 1 - delta));
            }));
        }

        // A move by class: no geid, succeeded or no answer, n > 0 units from a real source. Candidate o moves with
        // r_o = Moved_o / p_o, where p_o = P(X_o = s) on its current filter; no source likelihood, since it is not named.
        foreach (var m in moveList.Where(m => m.Geid is null && m.Result is null or "succeed" && m.Amount > 0
                     && !NotClassMoves.Contains(m.MoveType) && !m.MoveType.StartsWith("Query", StringComparison.Ordinal)))
        {
            if (SourceOf(m) is not { } s || TargetOf(m) is not { } t) continue;
            events.Add((m.Ts, 0, m.Provenance, seq =>
            {
                var candidates = byClass[m.ItemClass].Where(g => At(holders[g], s) is { } i && law[g][i] > 0)
                    .OrderByDescending(g => arrived.GetValueOrDefault((g, s), -1)).ToList();
                foreach (var g in candidates) Decay(g, m.Ts);   // p_o is read after D
                var presence = candidates.Select(g => law[g][holders[g].IndexOf(s)]).ToArray();
                var (moved, shortfall) = ClassMoveSplit.Split(presence, (int)m.Amount);
                var missing = shortfall.Select((p, k) => p * k).Sum();
                shortfalls.Add(missing);
                var take = Math.Min(missing, lots.GetValueOrDefault((s, m.ItemClass)));
                lots[(s, m.ItemClass)] = lots.GetValueOrDefault((s, m.ItemClass)) - take;
                lots[(t, m.ItemClass)] = lots.GetValueOrDefault((t, m.ItemClass)) + take;
                for (var i = 0; i < candidates.Count; i++)
                {
                    var r = Math.Min(1, moved[i] / presence[i]);   // presence > 0: candidates hold s with some mass
                    Apply(candidates[i], seq, m.Provenance, m.Ts, null, t, h => Shift(h, s, t, r));
                }
            }));
        }

        var ordered = events.OrderBy(e => e.Ts, StringComparer.Ordinal).ThenBy(e => e.Phase)
            .ThenBy(e => e.Provenance.SessionId).ThenBy(e => e.Provenance.LineOffset).ThenBy(e => e.Provenance.ItemIx).ToList();
        for (var seq = 0; seq < ordered.Count; seq++) ordered[seq].Run(seq);

        var chains = new Dictionary<string, IReadOnlyList<ChainEntry>>();
        foreach (var (geid, list) in steps)
        {
            var h = holders[geid];
            var (predictive, smoothed) = ObjectChain.Run(Prior(geid, h.Count),
                [.. list.SelectMany(e => e.Decay is { } d ? new[] { d(h), e.Kernel(h) } : [e.Kernel(h)])]);
            IReadOnlyDictionary<Holder, double> Law(double[] p) => h.Zip(p).ToDictionary(x => x.First, x => x.Second);
            var stride = hazards is null ? 1 : 2;   // the step's own row is the last of its (D, kernel) pair
            chains[geid] = [.. list.Select((e, t) => new ChainEntry(e.P, e.Ts, e.Source,
                t > 0 && e.Source?.Kind is "Container" or "Location" ? predictive[t * stride + stride - 1][h.IndexOf(e.Source!)] : null,
                Law(predictive[t * stride + stride - 1]), Law(smoothed[t * stride + stride - 1])))];
        }
        return (chains, shortfalls.Count == 0 ? 0 : shortfalls.Average(), lots);
    }

    // §4: a Store never names its source, which is always the player's hand or body.
    private static Holder? SourceOf(MoveEvidence m) =>
        m.MoveType == "Store" && m.SourceKind == "Invalid" ? Holder.Body
        : Sources.Contains(m.SourceKind ?? "") ? Of(m.SourceKind!, m.SourceKey) : null;

    private static Holder? TargetOf(MoveEvidence m) => Targets.Contains(m.TargetKind ?? "") ? Of(m.TargetKind!, m.TargetKey) : null;

    private static Holder Of(string kind, string? key) => kind == "Equipped" ? Holder.Body : new Holder(kind, key ?? "");

    private static int? At(List<Holder> holders, Holder? holder) =>
        holder is not null && holders.IndexOf(holder) is >= 0 and var i ? i : null;

    // B6: staying in a holder over `hours` keeps exp(−λ·hours), and B8: ρ' per game update without a reset; the rest goes
    // Out of sight, which never decays.
    private static ChainStep DecayStep(List<Holder> holders, Hazards rates, string cls, double hours, int updates)
    {
        var m = new double[holders.Count, holders.Count];
        var oos = holders.IndexOf(Holder.OutOfSight);
        for (var i = 0; i < holders.Count; i++)
        {
            var h = holders[i];
            var rate = h.Kind == "World" ? rates.Despawn
                : h.Kind != "Location" && Consumables.Any(p => cls.StartsWith(p, StringComparison.Ordinal)) ? rates.Consumption : rates.Leak;
            var stay = Math.Exp(-rate * hours) * Math.Pow(rates.Rho, updates);
            m[i, i] += stay;
            m[i, oos] += 1 - stay;
        }
        return new ChainStep(m, null);
    }

    // On row `from`, stay with 1 − r or go to `to` with r. Every other state stays where it is.
    private static ChainStep Shift(List<Holder> holders, Holder from, Holder to, double r)
    {
        var m = new double[holders.Count, holders.Count];
        for (var i = 0; i < holders.Count; i++) m[i, i] = 1;
        int f = holders.IndexOf(from), t = holders.IndexOf(to);
        m[f, f] -= r;
        m[f, t] += r;
        return new ChainStep(m, null);
    }
}
