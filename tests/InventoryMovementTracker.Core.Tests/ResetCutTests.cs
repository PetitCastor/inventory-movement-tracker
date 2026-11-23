using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class ResetCutTests
{
    private const double Eps = 0.01;
    private static readonly Holder Hand = new("Invalid", ""), B = new("Container", "B"), C = new("Container", "C"),
        Area18 = new("Location", "Area18");
    private long _line;

    private static string At(int second) => $"2026-11-01T10:00:{second:00}.0000000Z";

    private MoveEvidence Move(int second, string geid, Holder source, Holder target, string type = "Store") =>
        new(new Provenance(1, ++_line, 0), At(second), type, "cls", geid, 1, source.Kind, source.Key, target.Kind, target.Key, "succeed", null, null);

    // A session of build 1 that reset at `reset`, written without fractional seconds: instants are compared, not strings.
    private static SessionEvidence ResetSession(int reset) =>
        new(2, At(reset - 1), null, 1, "spawn", null, $"2026-11-01T10:00:{reset:00}Z");

    private static string Flat(IReadOnlyDictionary<Holder, double> law) =>
        string.Join(";", law.OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Key.Key).Select(kv => $"{kv.Key}={kv.Value:R}"));

    private static void AssertSame(IReadOnlyList<ChainEntry> expected, IEnumerable<ChainEntry> actual) =>
        Assert.Equal(
            expected.Select(e => (e.Provenance, e.Ts, e.Source, e.SourceProbability, Flat(e.Predictive), Flat(e.Smoothed))),
            actual.Select(e => (e.Provenance, e.Ts, e.Source, e.SourceProbability, Flat(e.Predictive), Flat(e.Smoothed))));

    [Fact]
    public void After_a_reset_each_geid_is_reborn_at_its_first_evidence()
    {
        // The 12519617 case in small: stored in two containers, the reset, then taken from them to Area18.
        var moves = new[] { Move(1, "g1", Hand, B), Move(2, "g2", Hand, C), Move(11, "g1", B, Area18), Move(12, "g2", C, Area18) };

        var (chains, shortfall) = ResetCut.Run(moves, [], [ResetSession(10)], Eps, Eps);

        Assert.Equal(2, shortfall.Count);
        foreach (var geid in new[] { "g1", "g2" })
        {
            Assert.Equal(2, chains[geid].Count);
            Assert.Equal(1, chains[geid][1].Predictive[Holder.OutOfSight]);   // nothing left on a holder from before
            Assert.Null(chains[geid][1].SourceProbability);                   // reborn: not a check
            Assert.True(chains[geid][1].Smoothed[Area18] > 0.98);
        }
    }

    [Fact]
    public void Evidence_after_the_reset_does_not_leak_backward()
    {
        var before = new[] { Move(1, "g", Hand, B), Move(2, "g", B, C, type: "Interaction") };
        var after = new[] { Move(11, "g", Area18, B, type: "Interaction") };   // contradicts C: smoothing would revise it

        var chain = ResetCut.Run([.. before, .. after], [], [ResetSession(10)], Eps, Eps).Chains["g"];

        AssertSame(NamedChains.Run(before, [], Eps, Eps).Chains["g"], chain.Take(2));
        Assert.Equal(3, chain.Count);
    }

    [Fact]
    public void Evidence_at_the_exact_instant_of_the_reset_is_after_it()
    {
        var chain = ResetCut.Run([Move(1, "g", Hand, B), Move(10, "g", B, Area18)], [], [ResetSession(10)], Eps, Eps).Chains["g"];

        Assert.Equal(1, chain[1].Predictive[Holder.OutOfSight]);
        Assert.Null(chain[1].SourceProbability);
    }

    [Fact]
    public void Without_a_reset_the_result_is_that_of_named_chains()
    {
        var moves = new[] { Move(1, "g", Hand, B), Move(2, "g", B, Area18) };
        var session = new SessionEvidence(1, At(0), null, 1, "spawn", null, null);

        var (chains, shortfall) = ResetCut.Run(moves, [], [session], Eps, Eps);
        var expected = NamedChains.Run(moves, [], Eps, Eps);

        AssertSame(expected.Chains["g"], chains["g"]);
        Assert.Equal([expected.MeanShortfall], shortfall);
    }

    [Fact]
    public void A_reset_after_a_spawn_of_the_same_build_is_not_counted()
    {
        // P0.1 §3: the creator counts only before the build's first spawn. Session 1 spawned in build 1 first.
        var spawned = new SessionEvidence(1, At(0), null, 1, "spawn", null, null);

        var (chains, shortfall) = ResetCut.Run([Move(1, "g", Hand, B), Move(11, "g", B, Area18)], [], [spawned, ResetSession(10)], Eps, Eps);

        Assert.Single(shortfall);
        Assert.Equal(1 - Eps, chains["g"][1].SourceProbability!.Value, 1e-12);
    }
}
