using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class NamedChainsHazardTests
{
    private const double Eps = 0.01;
    private static readonly Holder B = new("Container", "B"), S = new("Location", "S"), W = new("World", "floor");
    private static readonly Holder Hand = new("Invalid", "");
    private long _line;

    private static string At(double hours) => new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero).AddHours(hours).ToString("O");

    private MoveEvidence Move(double hours, Holder source, Holder target, string type = "Store", string cls = "Drink_x", string? result = "succeed") =>
        new(new Provenance(1, ++_line, 0), At(hours), type, cls, "g", 1, source.Kind, source.Key, target.Kind, target.Key, result, null, null);

    private static IReadOnlyList<ChainEntry> Run(IEnumerable<MoveEvidence> moves, Hazards? hazards) =>
        NamedChains.Run(moves, [], epsilon: Eps, delta: Eps, hazards: hazards).Chains["g"];

    [Fact]
    public void Null_hazards_change_nothing()
    {
        MoveEvidence[] moves = [Move(0, Hand, B), Move(5, B, S, "Interaction"), Move(30, S, Holder.Body, "Interaction")];

        var without = NamedChains.Run(moves, [], epsilon: Eps, delta: Eps).Chains["g"];
        var with = Run(moves, null);

        Assert.Equal(without.Count, with.Count);
        for (var t = 0; t < with.Count; t++)
        {
            Assert.Equal(without[t].SourceProbability, with[t].SourceProbability);
            Assert.Equal(without[t].Predictive.OrderBy(x => x.Key.Key), with[t].Predictive.OrderBy(x => x.Key.Key));
            Assert.Equal(without[t].Smoothed.OrderBy(x => x.Key.Key), with[t].Smoothed.OrderBy(x => x.Key.Key));
        }
    }

    [Fact]
    public void A_drink_in_a_container_is_consumed_between_proofs()
    {
        var chain = Run([Move(0, Hand, B), Move(24, B, Holder.Body, "Interaction")], new Hazards(1.0 / 24, 0, 0));

        var expected = (1 - Eps) * Math.Exp(-1);
        Assert.Equal(expected, chain[1].Predictive[B], 1e-9);
        Assert.Equal(expected, chain[1].SourceProbability!.Value, 1e-9);
    }

    [Fact]
    public void A_drink_in_a_station_only_leaks()
    {
        var chain = Run([Move(0, Hand, S), Move(24, S, Holder.Body, "Interaction")], new Hazards(1.0, 0, 1.0 / 48));

        Assert.Equal((1 - Eps) * Math.Exp(-0.5), chain[1].Predictive[S], 1e-9);
    }

    [Fact]
    public void A_dropped_item_despawns_on_the_floor()
    {
        MoveEvidence[] moves = [Move(0, Hand, W, "Drop", "Tool_x"), Move(1, W, Holder.Body, "Interaction", "Tool_x")];

        var before = Run(moves, null)[1].Predictive[W];
        var after = Run(moves, new Hazards(0, 1, 0, Hangar: 0))[1].Predictive[W];

        Assert.Equal(Math.Exp(-1), after / before, 1e-9);
    }

    [Fact]
    public void Out_of_sight_mass_does_not_decay()
    {
        // A lost move weighs its source and does not jump: the geid stays Out of sight until the next proof.
        var chain = Run([Move(0, Hand, S, "Interaction", result: "lost"), Move(10, Hand, S)], new Hazards(1, 1, 1));

        Assert.Equal(1, chain[1].Predictive[Holder.OutOfSight], 1e-12);
    }

    [Fact]
    public void Smoothing_sees_the_hazard()
    {
        MoveEvidence[] moves = [Move(0, Hand, B), Move(24, B, Holder.Body, "Interaction")];

        var plain = Run(moves, null)[0].Smoothed[B];
        var decayed = Run(moves, new Hazards(1.0 / 24, 0, 0))[0].Smoothed[B];

        Assert.True(Math.Abs(plain - decayed) > 1e-5);
    }
}
