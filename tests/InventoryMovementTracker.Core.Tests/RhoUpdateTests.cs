using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class RhoUpdateTests
{
    private const double Eps = 0.01;
    private static readonly Holder Hand = new("Invalid", ""), B = new("Container", "B"), S = new("Location", "S");
    private static readonly Hazards NoRates = new(0, 0, 0, 0.9);
    private long _line;

    private static string At(int second) => $"2026-11-01T10:00:{second:00}.0000000Z";

    private MoveEvidence Move(int second, Holder source, Holder target, string type = "Store") =>
        new(new Provenance(1, ++_line, 0), At(second), type, "cls", "g", 1, source.Kind, source.Key, target.Kind, target.Key, "succeed", null, null);

    // A session of `build` whose first line is at `second`; it spawned unless it opened the creator at `reset`.
    private static SessionEvidence Session(long id, int second, long build, int? reset = null) =>
        new(id, At(second), null, build, "spawn", null, reset is { } r ? At(r) : null);

    private static IReadOnlyList<ChainEntry> Run(MoveEvidence[] moves, SessionEvidence[] sessions, Hazards? hazards = null) =>
        ResetCut.Run(moves, [], sessions, Eps, Eps, hazards).Chains["g"];

    [Fact]
    public void An_update_without_a_reset_keeps_the_station_with_rho()
    {
        MoveEvidence[] moves = [Move(1, Hand, S), Move(9, S, Holder.Body, "Interaction")];
        SessionEvidence[] sessions = [Session(1, 0, 1), Session(2, 5, 2)];

        var plain = Run(moves, sessions)[1].Predictive[S];
        var chain = Run(moves, sessions, NoRates);

        Assert.Equal(0.9 * plain, chain[1].Predictive[S], 1e-9);
        Assert.Equal(0.9 * plain, chain[1].SourceProbability!.Value, 1e-9);
    }

    [Fact]
    public void Two_updates_in_the_interval_keep_rho_squared()
    {
        MoveEvidence[] moves = [Move(1, Hand, S), Move(9, S, Holder.Body, "Interaction")];
        SessionEvidence[] sessions = [Session(1, 0, 1), Session(2, 3, 2), Session(3, 6, 3)];

        var plain = Run(moves, sessions)[1].Predictive[S];

        Assert.Equal(0.81 * plain, Run(moves, sessions, NoRates)[1].Predictive[S], 1e-9);
    }

    [Fact]
    public void An_update_with_a_counted_reset_is_not_an_update_without_reset()
    {
        // Build 2 opens the creator at 10, before any spawn of its own: the reset is counted and cuts at 10, so the
        // step at 7 shares its segment with the step at 1 and the build change at 5 lies between them.
        MoveEvidence[] moves = [Move(1, Hand, B), Move(7, B, Holder.Body, "Interaction")];
        SessionEvidence[] sessions = [Session(1, 0, 1), Session(2, 5, 2, reset: 10)];

        var plain = Run(moves, sessions)[1].Predictive[B];

        Assert.Equal(plain, Run(moves, sessions, NoRates)[1].Predictive[B], 1e-9);
    }

    [Fact]
    public void An_update_before_a_geids_first_step_has_no_effect()
    {
        MoveEvidence[] moves = [Move(5, Hand, S), Move(9, S, Holder.Body, "Interaction")];
        SessionEvidence[] sessions = [Session(1, 0, 1), Session(2, 2, 2)];

        var plain = Run(moves, sessions)[1].Predictive[S];

        Assert.Equal(plain, Run(moves, sessions, NoRates)[1].Predictive[S], 1e-9);
    }

    [Fact]
    public void Without_hazards_reset_cut_is_unchanged()
    {
        MoveEvidence[] moves = [Move(1, Hand, S), Move(9, S, Holder.Body, "Interaction")];
        var expected = NamedChains.Run(moves, [], Eps, Eps).Chains["g"];

        var chain = Run(moves, [Session(1, 0, 1), Session(2, 5, 2)]);

        Assert.Equal(expected.Count, chain.Count);
        for (var t = 0; t < chain.Count; t++)
        {
            Assert.Equal(expected[t].SourceProbability, chain[t].SourceProbability);
            Assert.Equal(expected[t].Predictive.OrderBy(x => x.Key.Kind), chain[t].Predictive.OrderBy(x => x.Key.Kind));
            Assert.Equal(expected[t].Smoothed.OrderBy(x => x.Key.Kind), chain[t].Smoothed.OrderBy(x => x.Key.Kind));
        }
    }
}
