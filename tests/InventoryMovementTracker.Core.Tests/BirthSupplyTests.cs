using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class BirthSupplyTests
{
    private const double Tolerance = 1e-12;
    private static readonly Holder S = new("Container", "S"), T = new("Container", "T"), Y = new("Container", "Y"), C = new("Location", "C");
    private long _line;

    private MoveEvidence Move(string type, string? geid, Holder source, Holder target, long units = 1) =>
        new(new Provenance(1, ++_line, 0), $"2026-11-01T10:00:{_line:00}.0000000Z", type, "cls", geid, units,
            source.Kind, source.Key, target.Kind, target.Key, "succeed", null, null);

    private MoveEvidence Store(string geid, Holder target) => Move("Store", geid, new("Invalid", ""), target);

    private MoveEvidence ClassMove(Holder source, Holder target, long units = 1) => Move("Move", null, source, target, units);

    private static (IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Chains, double MeanShortfall, IReadOnlyDictionary<(Holder, string), double> Lots) Run(
        double epsilon, double birth, params MoveEvidence[] moves) => NamedChains.Run(moves, [], epsilon, delta: .01, birth: birth);

    private static (string?, double[]) Geid(string geid, double p) => (geid, [1 - p, p]);

    private static (string?, double[]) Lot(params double[] law) => (null, law);

    // Compares laws, whose arrays may differ by trailing zeros.
    private static void Same(double[] expected, double[] actual)
    {
        for (var i = 0; i < Math.Max(expected.Length, actual.Length); i++)
            Assert.Equal(expected.ElementAtOrDefault(i), actual.ElementAtOrDefault(i), Tolerance);
    }

    [Fact]
    public void Case_01_the_source_and_the_carriers_share_one_joint_law()
    {
        var (r, _, births) = ClassMoveSplit.Supply([Geid("g", .5), Geid("g", .5)], 1, sPlaces: 1, birth: .4);

        Same([1, .6], r);
        Same([.8, .2], births);
    }

    [Fact]
    public void Case_02_each_missing_unit_is_born_on_its_own()
    {
        var (r, _, births) = ClassMoveSplit.Supply([Geid("g", 1)], 2, sPlaces: 0, birth: .5);

        Same([.75], r);
        Same([0, .75, .25], births);
    }

    [Fact]
    public void Case_03_a_carrier_lot_serves_the_units_that_are_not_born()
    {
        var (_, lots, births) = ClassMoveSplit.Supply([Lot(0, 0, 1)], 2, sPlaces: 0, birth: .3);

        Same([.09, .42, .49], lots[0].Taken);
        Same([.49, .42, .09], lots[0].Left);
        Same([.49, .42, .09], births);
    }

    [Fact]
    public void Case_04_a_certain_birth_leaves_the_carriers_alone_and_never_replaces_the_source()
    {
        var (r, _, births) = ClassMoveSplit.Supply([Geid("g", 1)], 1, sPlaces: 0, birth: 1);

        Same([0], r);
        Same([0, 1], births);
        Same([0, 1], ClassMoveSplit.Supply([Lot(0, 1)], 2, sPlaces: 1, birth: .3).Short);
    }

    [Fact]
    public void Case_05_a_geid_counts_once_in_the_joint_law()
    {
        Same([1, .6, 0], ClassMoveSplit.Supply([Geid("g", .5), Geid("g", .5), Geid("h", 1)], 1, sPlaces: 1, birth: .4).R);
        Same([1, .3, 0], ClassMoveSplit.Supply([Geid("g", .5), Geid("h", 1), Geid("g", .5)], 1, sPlaces: 1, birth: .4).R);
    }

    [Fact]
    public void Case_06_a_unit_nobody_supplies_is_born_in_the_target_lot()
    {
        Assert.Equal(1, Run(.01, .74, ClassMove(S, T)).Lots[(T, "cls")], Tolerance);

        var (chains, _, lots) = Run(.01, .74, ClassMove(S, T), Move("Interaction", "new", T, Holder.Body));

        Assert.Equal(1, Assert.Single(chains["new"]).Predictive[T], Tolerance);   // never seen: no candidate of the class move
        Assert.Equal(0, lots[(T, "cls")], Tolerance);
    }

    [Fact]
    public void Case_07_a_move_without_a_real_source_is_all_births() =>
        Assert.Equal(2, Run(.01, .74, ClassMove(new("Invalid", ""), T, 2)).Lots.GetValueOrDefault((T, "cls")), Tolerance);

    [Fact]
    public void Case_08_a_move_to_its_own_source_adds_no_unit() =>
        Assert.All(Run(.01, .74, ClassMove(S, S)).Lots.Values, mean => Assert.Equal(0, mean, Tolerance));

    [Fact]
    public void Case_09_out_of_sight_serves_what_is_not_born()
    {
        var (chains, _, lots) = Run(.5, .4, Store("g", S), ClassMove(S, C));

        Assert.Equal(.2, chains["g"][^1].Smoothed[Holder.OutOfSight], Tolerance);
        Assert.Equal(.2, lots[(C, "cls")], Tolerance);
    }

    [Fact]
    public void Case_10_a_geid_at_three_places_is_left_out_of_its_own_demand()
    {
        var (r, _, births) = ClassMoveSplit.Supply([Geid("g", .3), Geid("g", .3), Geid("g", .3)], 1, sPlaces: 1, birth: .4);

        Same([1, .6, .6], r);
        Same([.66, .34], births);
    }

    [Fact]
    public void Case_11_rank_n_keeps_the_mass_of_larger_offers_in_the_joint_law()
    {
        var (_, lots, births) = ClassMoveSplit.Supply([Lot(.5, 0, .5), Lot(0, 1)], 1, sPlaces: 1, birth: .4);

        Same([.7, .3], lots[1].Taken);
        Same([.8, .2], births);
    }

    [Fact]
    public void Case_12_out_of_sight_serves_one_geid_after_the_other()
    {
        var (r, _, births) = ClassMoveSplit.Supply([Geid("g1", .5), Geid("g2", .5)], 1, sPlaces: 0, birth: .4);

        Same([.6, .3], r);
        Same([.45, .55], births);
    }

    [Fact]
    public void Case_12_out_of_sight_serves_in_ordinal_order()
    {
        // Neither the order of the evidence (b, a, c) nor the latest first (c, a, b). Each is Out of sight with 0.5.
        var chains = Run(.5, .4, Store("b", Y), Store("a", Y), Store("c", Y), ClassMove(S, T)).Chains;

        Assert.Equal(.5 * .6, chains["a"][^1].Smoothed.GetValueOrDefault(T), Tolerance);
        Assert.Equal(.5 * .3, chains["b"][^1].Smoothed.GetValueOrDefault(T), Tolerance);
        Assert.Equal(.5 * .15, chains["c"][^1].Smoothed.GetValueOrDefault(T), Tolerance);
    }

    [Fact]
    public void Case_13_crumbs_out_of_sight_do_not_arrive()
    {
        // g1 is last in ordinal order. Had the ten crumbs arrived in T, they would go before it: 0.96800397.
        var chains = Run(.01, .74, [Store("g1", T), .. Enumerable.Range(0, 10).Select(i => Store($"a{i}", Y)),
            ClassMove(S, T), ClassMove(T, C)]).Chains;

        Assert.Equal(.99240033, chains["g1"][^1].Smoothed[C], 1e-8);
    }

    [Fact]
    public void An_r_that_must_be_null_is_exactly_null()
    {
        // 1 − P(nothing wanted) would leave 1e-16 for the third geid: a target it does not reach, and an arrival there.
        var (r, _, _) = ClassMoveSplit.Supply([Lot(0, 1), Geid("a", .98), Geid("b", .98), Geid("c", .98)], 1, sPlaces: 1, birth: .74);

        Assert.All(r, x => Assert.Equal(0d, x));
    }
}
