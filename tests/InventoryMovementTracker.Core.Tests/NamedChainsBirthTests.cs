using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class NamedChainsBirthTests
{
    private const double Eps = 0.01;
    private static readonly Holder L = new("Location", "L"), C = new("Location", "C");
    private long _line;

    private static string At(int second) => $"2026-11-01T10:00:{second:00}.0000000Z";

    private MoveEvidence Buy(int second, long units, string result = "succeed") =>
        new(new Provenance(1, ++_line, 0), At(second), "Buy", "cls", null, units, "Shop", null, L.Kind, L.Key, result, null, 10);

    private MoveEvidence Take(int second, string geid) =>
        new(new Provenance(1, ++_line, 0), At(second), "Interaction", "cls", geid, 1, L.Kind, L.Key, "Corpse", "dead", "succeed", null, null);

    private MoveEvidence ClassMove(int second, long units) =>
        new(new Provenance(1, ++_line, 0), At(second), "Move", "cls", null, units, L.Kind, L.Key, C.Kind, C.Key, "succeed", null, null);

    private static (IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Chains, double MeanShortfall, IReadOnlyDictionary<(Holder, string), double> Lots) Run(params MoveEvidence[] moves) =>
        NamedChains.Run(moves, [], epsilon: Eps, delta: Eps);

    [Fact]
    public void A_bought_unit_then_moved_by_name_counts_once()
    {
        var (chains, _, lots) = Run(Buy(1, 1), Take(2, "g"));

        Assert.Equal(1, chains["g"][0].Predictive[L], 1e-9);
        Assert.Equal(0, lots.GetValueOrDefault((L, "cls")), 1e-9);
    }

    [Fact]
    public void One_geid_out_of_three_bought_units_leaves_two()
    {
        var (chains, _, lots) = Run(Buy(1, 3), Take(2, "g"));

        Assert.Equal(1, chains["g"][0].Predictive[L], 1e-9);
        Assert.Equal(2, lots[(L, "cls")], 1e-9);
    }

    [Fact]
    public void Without_a_purchase_the_first_evidence_keeps_the_out_of_sight_prior()
    {
        var (chains, _, lots) = Run(Take(2, "g"));

        Assert.Equal(1, chains["g"][0].Predictive[Holder.OutOfSight], 1e-9);
        Assert.Empty(lots);
    }

    [Fact]
    public void A_failed_purchase_gives_no_lot()
    {
        var (_, _, lots) = Run(Buy(1, 3, result: "failed"));

        Assert.Equal(0, lots.GetValueOrDefault((L, "cls")));
    }

    [Fact]
    public void A_class_move_without_candidates_takes_its_units_from_the_lot()
    {
        var (_, _, lots) = Run(Buy(1, 3), ClassMove(2, 2));

        Assert.Equal(1, lots[(L, "cls")], 1e-9);
        Assert.Equal(2, lots[(C, "cls")], 1e-9);
    }
}
