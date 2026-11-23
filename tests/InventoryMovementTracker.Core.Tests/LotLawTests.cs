using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class LotLawTests
{
    private const double Eps = 0.5, Tolerance = 1e-12;
    private static readonly Holder S = new("Location", "S"), T = new("Location", "T");
    private long _line;

    private MoveEvidence Evidence(string type, string? geid, long units, string sourceKind, string? sourceKey, Holder target) =>
        new(new Provenance(1, ++_line, 0), $"2026-11-01T10:00:{_line:00}.0000000Z", type, "cls", geid, units,
            sourceKind, sourceKey, target.Kind, target.Key, "succeed", null, null);

    private MoveEvidence Store(string geid) => Evidence("Store", geid, 1, "Invalid", null, S);

    private MoveEvidence Buy(long units) => Evidence("Buy", null, units, "Shop", null, S);

    private MoveEvidence ClassMove(long units, Holder target) => Evidence("Move", null, units, S.Kind, S.Key, target);

    private MoveEvidence Take(string geid) => Evidence("Interaction", geid, 1, S.Kind, S.Key, new("Corpse", "dead"));

    private static (IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Chains, double MeanShortfall, IReadOnlyDictionary<(Holder, string), double> Lots) Run(params MoveEvidence[] moves) =>
        NamedChains.Run(moves, [], epsilon: Eps, delta: Eps, birth: .4);

    // Compares laws, whose arrays may differ by trailing zeros.
    private static void SameLaw(double[] expected, double[] actual)
    {
        for (var i = 0; i < Math.Max(expected.Length, actual.Length); i++)
            Assert.Equal(expected.ElementAtOrDefault(i), actual.ElementAtOrDefault(i), Tolerance);
    }

    [Fact]
    public void Take_an_uncertain_demand_from_a_certain_lot()
    {
        var (taken, left) = LotLaw.Take([0, .25, .5, .25], [0, 0, 1]);

        SameLaw([0, .25, .75], taken);
        SameLaw([.75, .25], left);
    }

    [Fact]
    public void Take_an_uncertain_demand_from_an_uncertain_lot()
    {
        var (taken, left) = LotLaw.Take([.5, .5], [.5, .5]);

        SameLaw([.75, .25], taken);
        SameLaw([.75, .25], left);
    }

    [Fact]
    public void Add_convolves() => SameLaw([.375, .5, .125], LotLaw.Add([.75, .25], [.5, .5]));

    [Fact]
    public void A_class_move_takes_its_shortfall_law_from_the_lot()
    {
        var (chains, _, lots) = Run(Store("a"), Store("b"), Buy(2), ClassMove(3, T));

        // The law just before the class move: P(s) = 0.5 for each geid after its first evidence.
        Assert.Equal(.5, chains["a"][1].Predictive[S], Tolerance);
        Assert.Equal(.5, chains["b"][1].Predictive[S], Tolerance);
        Assert.Equal(.25, lots[(S, "cls")], Tolerance);
        Assert.Equal(1.85, lots[(T, "cls")], Tolerance);
    }

    [Fact]
    public void A_first_geid_is_a_lot_unit_with_the_probability_that_the_lot_is_not_empty()
    {
        var (chains, _, lots) = Run(Store("a"), Store("b"), Buy(2), ClassMove(3, T), Take("new"));

        Assert.Equal(.25, chains["new"][0].Predictive[S], Tolerance);
        Assert.Equal(.75, chains["new"][0].Predictive[Holder.OutOfSight], Tolerance);
        Assert.Equal(0, lots[(S, "cls")], Tolerance);
    }

    [Fact]
    public void A_first_geid_keeps_the_rest_of_the_law()
    {
        // L is 0, 1 or 2 with 1/4, 1/2, 1/4, then 0 or 1 with 3/4, 1/4 once the first geid left; a lot kept as its mean, 1,
        // would give the first geid 1 and the second 0.
        var (chains, _, lots) = Run(Store("a"), Store("b"), Buy(2), ClassMove(2, T), Take("new"), Take("other"));

        Assert.Equal(.75, chains["new"][0].Predictive[S], Tolerance);
        Assert.Equal(.25, chains["other"][0].Predictive[S], Tolerance);
        Assert.Equal(0, lots[(S, "cls")], Tolerance);
    }

    [Fact]
    public void A_class_move_to_its_own_source_leaves_the_lot_law_alone()
    {
        // Taking an uncertain T and adding it back as if independent would keep the mean, 1, but leave an empty lot
        // with 1/4; the geid that comes out next reads that.
        Assert.Equal(1, Run(Store("a"), Buy(1), ClassMove(1, S)).Lots[(S, "cls")], Tolerance);

        var (chains, _, lots) = Run(Store("a"), Buy(1), ClassMove(1, S), Take("new"));

        Assert.Equal(1, chains["new"][0].Predictive[S], Tolerance);
        Assert.Equal(0, lots[(S, "cls")], Tolerance);
    }
}
