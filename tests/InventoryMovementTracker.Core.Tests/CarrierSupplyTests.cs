using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class CarrierSupplyTests
{
    private const double Eps = 0.01, Tolerance = 1e-12;
    private static readonly Holder S = new("Container", "S"), T = new("Container", "T"), C = new("Location", "C");
    private long _line;

    private static string At(double hours) => new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero).AddHours(hours).ToString("O");

    private MoveEvidence Move(double hours, string type, string? geid, Holder source, Holder target, long units = 1) =>
        new(new Provenance(1, ++_line, 0), At(hours), type, "cls", geid, units, source.Kind, source.Key, target.Kind, target.Key, "succeed", null, null);

    private MoveEvidence Store(double hours, string geid, Holder target) => Move(hours, "Store", geid, new("Invalid", ""), target);

    private MoveEvidence Take(double hours, string geid, Holder source, Holder target) => Move(hours, "Interaction", geid, source, target);

    private MoveEvidence ClassMove(double hours, Holder source, Holder target) => Move(hours, "Move", null, source, target);

    private MoveEvidence Buy(double hours, Holder target) => Move(hours, "Buy", null, new("Shop", ""), target);

    private SightingEvidence Seen(double hours, string geid, string? parent = null) =>
        new(new Provenance(1, ++_line, 0), At(hours), geid, "cls", "Backpack", _line, parent, false);

    private static IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Run(
        MoveEvidence[] moves, SightingEvidence[] sightings, double epsilon = Eps, Hazards? hazards = null) =>
        NamedChains.Run(moves, sightings, epsilon, delta: Eps, hazards).Chains;

    private static (string?, double[]) Geid(string geid, double p) => (geid, [1 - p, p]);

    private static (string?, double[]) Lot(params double[] law) => (null, law);

    // Compares laws, whose arrays may differ by trailing zeros.
    private static void Same(double[] expected, double[] actual)
    {
        for (var i = 0; i < Math.Max(expected.Length, actual.Length); i++)
            Assert.Equal(expected.ElementAtOrDefault(i), actual.ElementAtOrDefault(i), Tolerance);
    }

    [Fact]
    public void Case_01_a_geid_never_competes_with_itself()
    {
        var (r, _, rest) = ClassMoveSplit.Supply([Geid("g", .5), Geid("g", .5)], 1);

        Same([1, 1], r);
        Same([1], rest);
    }

    [Fact]
    public void Case_02_the_body_covers_what_the_source_lacks()
    {
        var (r, _, rest) = ClassMoveSplit.Supply([Geid("g1", .5), Geid("g2", .8)], 1);

        Same([1, .5], r);
        Same([.9, .1], rest);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void Case_03_the_source_lot_serves_before_the_body(int n, double expected)
    {
        var (r, _, rest) = ClassMoveSplit.Supply([Lot(0, 1), Geid("g", 1)], n);

        Same([expected], r);
        Same([1], rest);
    }

    [Fact]
    public void Case_04_a_carrier_lot_takes_the_demand_the_geids_leave()
    {
        var (_, lots, _) = ClassMoveSplit.Supply([Geid("g", .5), Lot(0, 0, 1)], 2);

        Same([0, .5, .5], lots[0].Taken);
        Same([.5, .5], lots[0].Left);
    }

    [Fact]
    public void Case_05_a_certain_source_lot_leaves_nothing_to_the_body()
    {
        var (r, lots, _) = ClassMoveSplit.Supply([Geid("g", .5), Lot(0, 1), Geid("g", .5)], 1);

        Same([1, 0], r);
        Same([.5, .5], lots[0].Taken);
    }

    [Fact]
    public void Case_05b_a_geid_is_left_out_of_its_own_demand_past_a_lot()
    {
        var (r, lots, _) = ClassMoveSplit.Supply([Geid("g", .5), Lot(.5, .5), Geid("g", .5)], 1);

        Same([1, .5], r);
        Same([.75, .25], lots[0].Taken);
    }

    [Fact]
    public void Case_06_a_class_move_from_an_empty_container_takes_the_geid_on_the_body()
    {
        var chain = Run([ClassMove(1, S, T), Take(2, "g", T, Holder.Body)], [Seen(0, "g")])["g"];

        Assert.Equal(.99, chain[^1].SourceProbability!.Value, Tolerance);
    }

    [Fact]
    public void Case_07_a_move_to_its_own_source_leaves_the_body_alone()
    {
        var chain = Run([ClassMove(1, S, S), Take(2, "g", Holder.Body, C)], [Seen(0, "g")])["g"];

        Assert.Equal(.99, chain[^1].Predictive[Holder.Body], Tolerance);
    }

    [Fact]
    public void Case_08_a_candidate_that_does_not_move_keeps_its_hours_of_decay()
    {
        var chain = Run([Buy(0, S), ClassMove(10, S, T), Take(30, "g", Holder.Body, C)], [Seen(0, "g")],
            hazards: new Hazards(0, 0, .01, 1))["g"];

        Assert.Equal(.99 * Math.Exp(-.3), chain[^1].Predictive[Holder.Body], Tolerance);
        Assert.DoesNotContain(T, chain[^1].Predictive.Keys);
    }

    [Fact]
    public void Case_09_a_crumb_on_the_body_does_not_refresh_an_arrival()
    {
        // a: Body 0.99, then stored in T, where it keeps a crumb on the body. b: Body 0.99, all of which goes to T.
        var chains = Run([Store(1, "a", T), ClassMove(3, S, T), ClassMove(4, T, C)], [Seen(0, "a"), Seen(2, "b")]);

        Assert.Equal(.99, chains["b"][^1].Smoothed[C], Tolerance);
    }

    [Fact]
    public void Case_10_a_geid_counts_once_whatever_its_places()
    {
        Same([1, 1, 0], ClassMoveSplit.Supply([Geid("g", .5), Geid("g", .5), Geid("h", 1)], 1).R);
        Same([1, .5, 0], ClassMoveSplit.Supply([Geid("g", .5), Geid("h", 1), Geid("g", .5)], 1).R);
    }

    [Fact]
    public void Case_11_rank_n_keeps_the_mass_of_larger_offers()
    {
        var (_, lots, rest) = ClassMoveSplit.Supply([Lot(.5, 0, .5), Lot(0, 1)], 1);

        Same([.5, .5], lots[1].Taken);
        Same([1], rest);
    }

    [Fact]
    public void Case_12_the_kernel_applies_without_an_arrival()
    {
        // ε = 0.5. g: seen on the body (Body 0.5), then in T (T 0.5, Body 0.25, Out of sight 0.25). h: stored in T after it
        // (T 0.5). The move S→T takes g's body mass with r = 1: T = 0.75; 0.25 < 0.5, so h stays T's latest arrival.
        // The move T→C takes h with r = 1, then g with r = 1 − 0.5: 0.75 · 0.5 = 0.375 (0.75 if g had been refreshed).
        var chains = Run([Store(2, "h", T), ClassMove(3, S, T), ClassMove(4, T, C)], [Seen(0, "g"), Seen(1, "g", T.Key)], epsilon: .5);

        Assert.Equal(.75, chains["g"][^1].Predictive[T], Tolerance);
        Assert.Equal(.375, chains["g"][^1].Smoothed[C], Tolerance);
        Assert.Equal(.5, chains["h"][^1].Smoothed[C], Tolerance);
    }

    [Theory]
    [InlineData("S", .99)]   // B, worn through P, serves the empty S
    [InlineData("P", 0)]     // B is inside the source itself
    public void Case_13_a_carried_container_serves_unless_it_is_inside_the_source(string source, double expected)
    {
        // P and B are of another class, so that only g can answer the class move.
        var chain = Run([ClassMove(3, new("Container", source), T), Take(4, "g", T, Holder.Body)],
            [Seen(0, "P") with { ClassName = "bag" }, Seen(1, "B", "P") with { ClassName = "bag" }, Seen(2, "g", "B")],
            hazards: new Hazards(0, 0, 0, 1))["g"];

        Assert.Equal(expected, chain[^1].SourceProbability!.Value, Tolerance);
        Assert.Equal(.99 - expected, chain[^1].Predictive[new("Container", "B")], Tolerance);
    }
}
