using System.Globalization;

namespace InventoryMovementTracker.Core;

/// <summary>
/// Turns the evidence into one <see cref="ObjectChain"/> per geid: P0.1 §3, §4 and §6, reduced to mandates B4b and
/// B9b. A geid's states are the holders its own evidence names, plus Out of sight, where it starts, plus the targets
/// of the class moves it may have followed.
/// <para>
/// A move by class (no geid) is split with <see cref="ClassMoveSplit"/> among the named geids of its class that may be
/// at its source, on a carrier or Out of sight, so it needs every geid's current filter at once: one online forward pass
/// runs over all the evidence in time order and fixes each class move's kernels there. Each chain is then smoothed with those kernels as
/// they are. This frozen coupling is the approximation of decision B1, constraint 3: what a later step teaches about
/// one geid does not revise how an earlier class move was shared with the others.
/// </para>
/// <para>
/// A class move is served in a fixed order of places: the source, then, unless the source is the target, the body, then
/// the carried containers by key; at each, its geids, latest arrival first, then its lot (<see cref="LotLaw"/>). Arrival
/// is approximated by the last step that targeted the place. A container is carried, a hard decision, when the most
/// probable holders, as of each container's last step, lead from it to the body in 6 hops at most without passing
/// through the source or the target. Geids and lots are taken as independent of one another. Each unit the source lacks
/// is born in the target's lot with probability <c>birth</c>; the others are asked of the carriers, then of the geids seen
/// before that are Out of sight, in ordinal order since nothing there has a recency, and born if these lack them too. A
/// birth is drawn before the carriers serve, so the share of births exceeds <c>birth</c>. A move without a real source is
/// all births. Out of scope: resets (B7). Continuous hazards (B6) and the game updates without a reset (B8,
/// <c>updates</c>) apply only when <c>hazards</c> is given.
/// </para>
/// <para>
/// Lost moves come in queues (§4), from the first one to the next answer, succeed or failed, or change of session. A
/// queue proves that a source held, at its start, the most units its lost moves had asked for net of what earlier ones
/// would have brought; the lot becomes the larger of itself and what the geids there leave of that, two laws taken as
/// independent. Nothing moves. A move without an answer neither closes the queue nor counts in it, and an object asked
/// for again in the queue counts each time (R27).
/// </para>
/// <para>
/// A MoveAll takes from its source, with 1 − <c>epsCap</c> each, the geids, save its target and the containers that hold
/// it, and the units of the lots. Those containers are a hard decision: the most probable holders, as of each one's last
/// step, 6 levels up at most. What a lot sends and what it keeps are two laws, and the target's lot receives the former
/// as if independent (R26): the means are exact, not the laws.
/// </para>
/// </summary>
public static class NamedChains
{
    /// <summary>
    /// π_naiss (P0.1 §7), learned on the frozen corpus: its complete listings before 2026-10-01, both outcomes, listed and
    /// absent, rather than the 42 first appearances out of 57 units. Flat between 0.95 and 0.99.
    /// </summary>
    public const double DefaultBirth = 0.97;

    /// <summary>ε_cap (P0.1 §7): the chance that a MoveAll's full target leaves a unit behind. A declared choice.</summary>
    public const double DefaultEpsCap = 0.01;

    private static readonly HashSet<string> NamedMoves = ["Store", "Interaction", "StoreItem", "Drop", "Death"];
    private static readonly HashSet<string> NotClassMoves = ["Split", "Stack", "StackAll", "Sort", "AmmoRepool", "Buy", "MoveAll"];
    private static readonly string[] Consumables = ["Drink_", "Food_", "crlf_consumable_"];
    private static readonly HashSet<string> Sources = ["Container", "Location", "Equipped"];   // Invalid: no likelihood
    private static readonly HashSet<string> Targets = ["Container", "Location", "Equipped", "Corpse", "World"];

    /// <returns>
    /// Lots: the mean, at the end, of the law of the units per (holder, class) that no geid names. A successful Buy
    /// shifts that law by its Amount; a class move takes T = min(K, L) from each lot L of its places, K being the units the
    /// places before it leave wanted, and adds T and its births to the target lot; a geid first seen on a named move out of a lot is that lot's unit with P(L ≥ 1), and the
    /// lot becomes max(0, L − 1). This is an approximation: K and L, and the target lot and each T, are taken as independent,
    /// though one class move shares its units between the geids and the lots; adding several T and the births, as if independent, keeps
    /// the target lot's mean exact, not its law (n = 3 and a source lot of 0 or 1 unit, 0.5 each: the gain is 3, the sum gives
    /// 2, 3 and 4 with 0.25, 0.5 and 0.25). Chains: each geid's steps in evidence order (by ts, then moves before listings, then provenance), class moves
    /// included. MeanShortfall: the expected number of units per class move that no named geid of its source supplied.
    /// </returns>
    public static (IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Chains, double MeanShortfall, IReadOnlyDictionary<(Holder, string), double> Lots) Run(
        IEnumerable<MoveEvidence> moves, IEnumerable<SightingEvidence> sightings, double epsilon, double delta, Hazards? hazards = null,
        IReadOnlyList<DateTimeOffset>? updates = null, double birth = DefaultBirth, double epsCap = DefaultEpsCap)
    {
        // A named move that succeeded or got no answer weighs its source, then jumps to its target; a Drop's target is
        // the floor under its key as stored. failed: nothing. lost: the source only, no jump, unless its queue may have filled it.
        // A seen line, placeholders aside, is named evidence: a jump to the body, or to its parent container.
        var moveList = moves.ToList();
        var (queues, unweighed) = LostQueues(moveList);
        var seen = sightings.Where(s => !s.Placeholder).ToList();
        var named = moveList
            .Where(m => m.Geid is not null && NamedMoves.Contains(m.MoveType) && m.Result != "failed")
            .Select(m => (Geid: m.Geid!, Class: m.ItemClass, m.Ts, Phase: 0, m.Provenance, Source: unweighed.Contains(m) ? null : SourceOf(m),
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
        var lots = new Dictionary<(Holder, string), double[]>();
        double[] Lot(Holder holder, string cls) => lots.GetValueOrDefault((holder, cls), [1]);   // no lot: 0 units
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

        void Know(string geid, Holder holder)
        {
            if (!holders[geid].Contains(holder)) { holders[geid].Add(holder); law[geid] = [.. law[geid], 0]; }
        }

        void Apply(string geid, int seq, Provenance p, string ts, Holder? source, Holder? target, Func<List<Holder>, ChainStep> kernel)
        {
            Decay(geid, ts);
            if (target is not null) Know(geid, target);
            law[geid] = ObjectChain.Filter(law[geid], kernel(holders[geid]), $"at {ts}");
            steps[geid].Add((p, ts, source, kernel, pending.Remove(geid, out var decay) ? decay : null));
            if (target is not null) arrived[(geid, target)] = seq;
        }

        var events = named.Select(e => (e.Ts, e.Phase, e.Provenance, Run: (Action<int>)(seq =>
        {
            if (!steps[e.Geid].Any() && e.Source is { } s0 && 1 - Lot(s0, e.Class)[0] is var q and > 0)
            {
                births[e.Geid] = (s0, q);
                lots[(s0, e.Class)] = LotLaw.Take([0, 1], Lot(s0, e.Class)).Left;
                law[e.Geid] = Prior(e.Geid, law[e.Geid].Length);
            }
            Apply(e.Geid, seq, e.Provenance, e.Ts, e.Source, e.Target, h => ChainStep.Move(h.Count, At(h, e.Source), At(h, e.Target), epsilon));
        }))).ToList();

        // Before every event of the same key, since a queue bounds the stock at its start.
        events.InsertRange(0, queues.Select(q => (q.First.Ts, 0, q.First.Provenance, (Action<int>)(_ =>
        {
            foreach (var ((at, cls), (_, _, k)) in q.Counts.Where(c => c.Value.Proved > 0))
            {
                var presence = byClass[cls].Select(g => Mass(g, at)).ToList();   // a geid without a step is Out of sight: 0
                lots[(at, cls)] = LotLaw.Max(Lot(at, cls), ClassMoveSplit.Split(presence, k).Shortfall);
            }
        }))));

        // A successful Buy: its Amount is born in the target, which already is the landing station.
        foreach (var m in moveList.Where(m => m.MoveType == "Buy" && m.Result == "succeed" && m.Amount > 0))
            if (TargetOf(m) is { } at)
                events.Add((m.Ts, 0, m.Provenance, _ => lots[(at, m.ItemClass)] = LotLaw.Add(Lot(at, m.ItemClass), [.. new double[m.Amount], 1])));

        // A burst with the Body_ItemPort line is a complete listing: a geid that may be on the body and that the listing
        // leaves out goes from the body to Out of sight with 1 − δ.
        foreach (var burst in seen.GroupBy(s => (s.Provenance.SessionId, s.BurstId)))
        {
            if (burst.FirstOrDefault(s => s.Port == "Body_ItemPort") is not { } body) continue;
            var listed = burst.Select(s => s.Geid).ToHashSet();
            events.Add((body.Ts, 1, body.Provenance, seq =>
            {
                foreach (var geid in law.Keys.Where(g => !listed.Contains(g) && At(holders[g], Holder.Body) is { } b && law[g][b] > 0).ToList())
                    Apply(geid, seq, body.Provenance, body.Ts, null, null, h => Shift(h, [(Holder.Body, 1 - delta)], Holder.OutOfSight));
            }));
        }

        double Mass(string geid, Holder holder) => At(holders[geid], holder) is { } i ? law[geid][i] : 0;

        // The holder itself, then the most probable holder of each container.
        IEnumerable<Holder> Up(Holder holder)
        {
            for (var hops = 0; hops <= 6; hops++)   // a cycle of containers ends here too
            {
                yield return holder;
                if (holder.Kind != "Container" || !law.TryGetValue(holder.Key, out var p)) yield break;
                holder = holders[holder.Key][Array.IndexOf(p, p.Max())];
            }
        }

        bool Carried(Holder holder, Holder s, Holder t) =>
            Up(holder).TakeWhile(h => h == Holder.Body || (h != s && h != t)).Contains(Holder.Body);

        foreach (var m in moveList.Where(m => m.MoveType == "MoveAll" && m.Result is null or "succeed"))
        {
            if (SourceOf(m) is not { } s || TargetOf(m) is not { } t || s == t) continue;
            events.Add((m.Ts, 0, m.Provenance, seq =>
            {
                var up = Up(t).Where(h => h.Kind == "Container").Select(h => h.Key).ToHashSet();
                foreach (var g in law.Keys.Where(g => !up.Contains(g) && Mass(g, s) > 0).ToList())
                    Apply(g, seq, m.Provenance, m.Ts, null, t, h => Shift(h, [(s, 1 - epsCap)], t));
                foreach (var (_, cls) in lots.Keys.Where(k => k.Item1 == s).ToList())
                {
                    var lot = Lot(s, cls);
                    lots[(s, cls)] = LotLaw.Thin(lot, epsCap);
                    lots[(t, cls)] = LotLaw.Add(Lot(t, cls), LotLaw.Thin(lot, 1 - epsCap));
                }
            }));
        }

        // A move by class: no geid, succeeded or no answer, n > 0 units to a real target. A candidate leaves each of its
        // places with that place's r, all in one kernel; no source likelihood, since it is not named.
        foreach (var m in moveList.Where(m => ByClass(m) && m.Result is null or "succeed"))
        {
            if (TargetOf(m) is not { } t) continue;
            var s = SourceOf(m);   // none real: no place, so every unit is born
            events.Add((m.Ts, 0, m.Provenance, seq =>
            {
                // Only the holders known for the class can supply it. The body's empty key sorts it before the containers.
                var carriers = byClass[m.ItemClass].SelectMany(g => holders[g])
                    .Concat(lots.Keys.Where(k => k.Item2 == m.ItemClass).Select(k => k.Item1)).Distinct()
                    .Where(h => h != s && h != t && Carried(h, s!, t)).OrderBy(h => h.Key, StringComparer.Ordinal);
                List<Holder> from = s is null ? [] : s == t ? [s] : [s, .. carriers, Holder.OutOfSight];
                var places = from.SelectMany(h => byClass[m.ItemClass].Where(g => steps[g].Any() && Mass(g, h) > 0)
                    .OrderByDescending(g => arrived.GetValueOrDefault((g, h), -1)).ThenBy(g => g, StringComparer.Ordinal)
                    .Select(g => (At: h, Geid: (string?)g)).Concat(h == Holder.OutOfSight ? [] : [(At: h, Geid: null)])).ToList();
                var candidates = places.Where(p => p.Geid is not null).Select(p => (p.At, Geid: p.Geid!)).ToList();
                foreach (var (_, g) in candidates) Decay(g, m.Ts);   // the masses are read after D
                var supply = ClassMoveSplit.Supply([.. places.Select(p => (p.Geid,
                    p.Geid is { } g ? [1 - Mass(g, p.At), Mass(g, p.At)] : Lot(p.At, m.ItemClass)))], (int)m.Amount, places.Count(p => p.At == s), birth);
                var presence = candidates.Where(p => p.At == s).Select(p => Mass(p.Geid, p.At)).ToList();
                if (s is not null) shortfalls.Add(Mean(ClassMoveSplit.Split(presence, (int)m.Amount).Shortfall));
                if (s != t)   // L − T and T are not independent: adding T back would spread the lot's law
                {
                    foreach (var (h, (taken, left)) in from.Zip(supply.Lots))   // Out of sight, last, has no lot
                    {
                        lots[(h, m.ItemClass)] = left;
                        lots[(t, m.ItemClass)] = LotLaw.Add(Lot(t, m.ItemClass), taken);
                    }
                    lots[(t, m.ItemClass)] = LotLaw.Add(Lot(t, m.ItemClass), supply.Short);
                }
                foreach (var rows in candidates.Zip(supply.R, (p, r) => (p.Geid, p.At, R: r)).GroupBy(x => x.Geid))
                {
                    // A crumb from a carrier is no arrival where the geid already has more mass; nor is an empty move, nor what
                    // comes from Out of sight, where every geid leaves a crumb: the kernel moves it, without a recency.
                    var arrives = Mass(rows.Key, s!) > 0
                        || rows.Where(x => x.At != Holder.OutOfSight).Sum(x => x.R * Mass(x.Geid, x.At)) > Mass(rows.Key, t);
                    if (rows.Any(x => x.R * Mass(x.Geid, x.At) > 0)) Know(rows.Key, t);
                    Apply(rows.Key, seq, m.Provenance, m.Ts, null, arrives ? t : null, h => Shift(h, rows.Select(x => (x.At, x.R)), t));
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
        return (chains, shortfalls.Count == 0 ? 0 : shortfalls.Average(), lots.ToDictionary(kv => kv.Key, kv => Mean(kv.Value)));
    }

    private static bool ByClass(MoveEvidence m) => m.Geid is null && m.Amount > 0
        && !NotClassMoves.Contains(m.MoveType) && !m.MoveType.StartsWith("Query", StringComparison.Ordinal);

    // Per queue, its first lost move and, per (holder, class), the units asked of the holder, those that the queue would
    // have brought there, and those it proves: the most asked so far less those brought before. Unweighed: the lost moves
    // from a source that an earlier move of their queue may have filled.
    private static (List<(MoveEvidence First, Dictionary<(Holder, string), (int Asked, int Brought, int Proved)> Counts)> Queues, HashSet<MoveEvidence> Unweighed)
        LostQueues(List<MoveEvidence> moves)
    {
        var queues = new List<(MoveEvidence First, Dictionary<(Holder, string), (int Asked, int Brought, int Proved)> Counts)>();
        var unweighed = new HashSet<MoveEvidence>();
        var open = false;
        foreach (var m in moves.OrderBy(m => m.Ts, StringComparer.Ordinal).ThenBy(m => m.Provenance.SessionId)
                     .ThenBy(m => m.Provenance.LineOffset).ThenBy(m => m.Provenance.ItemIx))
        {
            open = open && m.Result is not ("succeed" or "failed") && m.Provenance.SessionId == queues[^1].First.Provenance.SessionId;
            if (m.Result != "lost" || m.MoveType == "MoveAll") continue;
            if (!open) queues.Add((m, []));
            open = true;
            var (s, t, cls, counts) = (SourceOf(m), TargetOf(m), m.ItemClass, queues[^1].Counts);
            var isNamed = NamedMoves.Contains(m.MoveType) && m.Geid is not null && s is not null;
            if (!isNamed && !(ByClass(m) && t is not null)) continue;
            var n = isNamed ? 1 : (int)m.Amount;
            if (s is not null)
            {
                var (asked, brought, proved) = counts.GetValueOrDefault((s, cls));
                counts[(s, cls)] = (asked + n, brought, Math.Max(proved, asked + n - brought));
                if (brought > 0) unweighed.Add(m);
            }
            if (t is not null)
            {
                var at = counts.GetValueOrDefault((t, cls));
                counts[(t, cls)] = at with { Brought = at.Brought + n };
            }
        }
        return (queues, unweighed);
    }

    private static double Mean(double[] law) => law.Select((p, k) => p * k).Sum();

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

    // On each row `From`, stay with 1 − r or go to `to` with r. Every other state stays where it is; so does every state
    // when the geid does not know `to`, since nothing arrives there.
    private static ChainStep Shift(List<Holder> holders, IEnumerable<(Holder From, double R)> rows, Holder to)
    {
        var m = new double[holders.Count, holders.Count];
        for (var i = 0; i < holders.Count; i++) m[i, i] = 1;
        if (At(holders, to) is { } t)
            foreach (var (from, r) in rows)
            {
                var f = holders.IndexOf(from);
                m[f, f] -= r;
                m[f, t] += r;
            }
        return new ChainStep(m, null);
    }
}
