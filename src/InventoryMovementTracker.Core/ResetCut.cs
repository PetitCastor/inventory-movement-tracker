using System.Globalization;

namespace InventoryMovementTracker.Core;

/// <summary>
/// P0.1 §3, mandate B7: a character reset cuts the evidence stream. Evidence at t goes to the segment of the last
/// reset ≤ t, so evidence at the reset's own instant is after it. <see cref="NamedChains"/> runs on each segment
/// alone: nothing on one side of a reset informs the other, and each geid is reborn at its first evidence after it.
/// <para>
/// A reset counts only if the creator opened before the build's first spawn. The old ingestion keeps it per session
/// (LogIngestor.cs:607); the per-build rule is taken from HoldingResolver.cs:1053: no session of the same build with an
/// earlier FirstTs has a FirstLocId. Unlike there, a session without FirstTs is left out of that test.
/// </para>
/// </summary>
public static class ResetCut
{
    /// <returns>
    /// Chains: each geid's entries, segment after segment. MeanShortfall: NamedChains' value for each segment, in order.
    /// </returns>
    public static (IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Chains, IReadOnlyList<double> MeanShortfall) Run(
        IEnumerable<MoveEvidence> moves, IEnumerable<SightingEvidence> sightings, IReadOnlyList<SessionEvidence> sessions,
        double epsilon, double delta, Hazards? hazards = null)
    {
        var counted = sessions
            .Where(s => s.CharResetAt is not null && !sessions.Any(o => o.Build == s.Build && o.FirstLocId is not null
                && o.FirstTs is not null && s.FirstTs is not null && Instant(o.FirstTs) < Instant(s.FirstTs))).ToList();
        var resets = counted.Select(s => Instant(s.CharResetAt!)).ToList();
        // B8: in FirstTs order, a session whose build differs from the previous one's and has no counted reset is a game
        // update at its FirstTs. Sessions without FirstTs or Build are left out.
        var known = sessions.Where(s => s.FirstTs is not null && s.Build is not null).OrderBy(s => Instant(s.FirstTs!)).ToList();
        var updates = known.Skip(1).Where((s, i) => s.Build != known[i].Build && counted.All(r => r.Build != s.Build))
            .Select(s => Instant(s.FirstTs!)).ToList();
        int Segment(string ts) => resets.Count(r => r <= Instant(ts));

        var movesBySegment = moves.ToLookup(m => Segment(m.Ts));
        var sightingsBySegment = sightings.ToLookup(s => Segment(s.Ts));
        var chains = new Dictionary<string, IReadOnlyList<ChainEntry>>();
        var shortfalls = new List<double>();
        for (var k = 0; k <= resets.Count; k++)
        {
            var r = NamedChains.Run(movesBySegment[k], sightingsBySegment[k], epsilon, delta, hazards, updates);
            foreach (var (geid, entries) in r.Chains) chains[geid] = [.. chains.GetValueOrDefault(geid, []), .. entries];
            shortfalls.Add(r.MeanShortfall);
        }
        return (chains, shortfalls);
    }

    private static DateTimeOffset Instant(string ts) => DateTimeOffset.Parse(ts, CultureInfo.InvariantCulture);
}
