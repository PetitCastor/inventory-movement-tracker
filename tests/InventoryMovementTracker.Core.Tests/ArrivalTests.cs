using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class ArrivalTests
{
    private const double Eps = 0.01, Tolerance = 1e-9;
    private static readonly Holder Hand = new("Invalid", ""), S = new("Location", "S"), U = new("Location", "U"), W = new("Location", "W"),
        T = new("Container", "t"), V = new("Container", "v"), Body = Holder.Body;
    private long _line;

    private string Now() => new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero).AddMinutes(_line).ToString("O");

    private MoveEvidence Move(string type, string? geid, Holder source, Holder target, long units = 1) =>
        new(new Provenance(1, ++_line, 0), Now(), type, "cls", geid, units, source.Kind, source.Key, target.Kind, target.Key, "succeed", null, null);

    private MoveEvidence ClassMove(Holder source, Holder target, long units = 1) => Move("Move", null, source, target, units);

    private MoveEvidence Take(string geid, Holder source, Holder target) => Move("Interaction", geid, source, target);

    private MoveEvidence StoreItem(string geid, Holder target) => Move("StoreItem", geid, Hand, target);

    private SightingEvidence Seen(string geid) => new(new Provenance(1, ++_line, 0), Now(), geid, "cls", "Backpack", _line, null, false);

    private static (IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Chains, double MeanShortfall, IReadOnlyDictionary<(Holder, string), double> Lots) Run(
        IEnumerable<MoveEvidence> moves, IEnumerable<SightingEvidence>? seen = null, Hazards? hazards = null) =>
        NamedChains.Run(moves, seen ?? [], Eps, delta: .02, hazards);

    private static double Lot(Holder at, IEnumerable<MoveEvidence> moves, params SightingEvidence[] seen) =>
        Run(moves, seen).Lots.GetValueOrDefault((at, "cls"));

    [Fact]
    public void Case_01_the_only_unit_of_a_class_move_is_the_geid_seen_where_it_went()
    {
        var move = ClassMove(S, Body);
        var g = Seen("g");

        Assert.Equal(1, Lot(Body, [move]), Tolerance);
        Assert.Equal(0, Lot(Body, [move], g), Tolerance);
        Assert.Equal(1, Run([move], [g]).Chains["g"][0].Predictive[Holder.OutOfSight], Tolerance);
    }

    [Fact]
    public void Case_02_two_units_and_two_geids()
    {
        var move = ClassMove(S, Body, 2);
        SightingEvidence[] seen = [Seen("g"), Seen("h")];

        Assert.Equal(2, Lot(Body, [move]), Tolerance);
        Assert.Equal(1, Lot(Body, [move], seen[0]), Tolerance);
        Assert.Equal(0, Lot(Body, [move], seen), Tolerance);
    }

    [Fact]
    public void Case_03_one_unit_and_two_geids_never_goes_below_zero()
    {
        var move = ClassMove(S, Body);
        SightingEvidence[] seen = [Seen("g"), Seen("h")];

        Assert.Equal(0, Lot(Body, [move], seen[0]), Tolerance);
        Assert.Equal(0, Lot(Body, [move], seen), Tolerance);
    }

    [Fact]
    public void Case_04_a_geid_seen_again_where_it_is_takes_only_its_m()
    {
        // Two units. The first sight takes one (m = 1). It leaves g at the body with 1 − ε, Out of sight with ε = .01: m = .01 for the
        // second, so the lot of one unit keeps it with .99 and loses it with .01: mean .99.
        var move = ClassMove(S, Body, 2);

        Assert.Equal(.99, Lot(Body, [move], Seen("g"), Seen("g")), Tolerance);
    }

    [Fact]
    public void Case_05_a_geid_known_elsewhere_takes_only_its_m()
    {
        // Take W to U from Out of sight: the source weighs ε against 1 − ε, then the jump keeps ε at Out of sight and sends 1 − ε to U.
        // Out of sight: ε·ε = 1e-4, U: ε(1 − ε) = .0099. Out of sight is .01 of .01: m = .01. Two units, the lot loses one with .01: 1.99.
        var move = ClassMove(S, Body, 2);

        Assert.Equal(1.99, Lot(Body, [move, Take("g", W, U)], Seen("g")), Tolerance);
    }

    [Fact]
    public void Case_06_a_named_move_with_a_weighed_source_takes_nothing_from_its_target()
    {
        MoveEvidence[] moves = [ClassMove(S, U), Move("Store", "g", Body, U)];

        Assert.Equal(1, Lot(U, moves), Tolerance);
    }

    [Fact]
    public void Case_07_a_store_item_without_source_takes_the_unit()
    {
        MoveEvidence[] moves = [ClassMove(S, U), StoreItem("g", U)];

        Assert.Equal(0, Lot(U, moves), Tolerance);
    }

    [Fact]
    public void Case_08_m_is_read_after_the_decay()
    {
        // The first sight takes one unit of two and leaves g at the body with .99, Out of sight with .01. Ten hours later with a leak of
        // .1 per hour, it stays at the body with e^-1: .99·e^-1, so Out of sight with 1 − .99·e^-1 = m. The lot of one unit keeps it with 1 − m:
        // mean .99·e^-1 = .3642006467597. Read before D, it would be .99.
        var move = ClassMove(S, Body, 2);
        var first = Seen("g");
        var second = Seen("g") with { Ts = DateTimeOffset.Parse(first.Ts).AddHours(10).ToString("O") };

        Assert.Equal(.3642006467597, Run([move], [first, second], new Hazards(0, 0, .1)).Lots[(Body, "cls")], Tolerance);
    }

    [Fact]
    public void Case_09_the_list_of_a_geid_born_of_a_lot_carries_the_arrivals_that_emptied_it()
    {
        // Two units in U, o arrives (m = 1): one left. o arrives again: m = .01 (Out of sight after its first line), the lot keeps its unit with
        // .99. g, named from U, is born of it with q = 1 − P(0 units) = .99: prior (Out of sight, U) = (.01, .99). Its move U to T weighs
        // Out of sight .01·ε·ε = 1e-6, U .99·(1 − ε)·ε = .009801, T .01·ε(1 − ε) + .99·(1 − ε)² = .970398; U is .009801 / .9802 of it.
        MoveEvidence[] moves = [ClassMove(S, U, 2), StoreItem("o", U), StoreItem("o", U), Take("g", U, T), Take("g", U, V)];

        Assert.Equal(.009801 / .9802, Run(moves).Chains["g"][1].SourceProbability!.Value, Tolerance);
        Assert.Equal(1, Checks(moves));
    }

    [Fact]
    public void Case_09b_the_list_of_a_lot_carries_the_list_of_the_geid_that_arrived()
    {
        // o is named before it arrives in U (Take W to T: Out of sight .01), so m = .01 there and the lot of one unit keeps it with .99, as above.
        // Without o's own line (the Take), a replay would see o arrive for the first time (m = 1), the lot empty, and g not born of it.
        MoveEvidence[] moves = [ClassMove(S, U), Take("o", W, T), StoreItem("o", U), Take("g", U, V), Take("g", U, W)];

        Assert.Equal(.009801 / .9802, Run(moves).Chains["g"][1].SourceProbability!.Value, Tolerance);
        Assert.Equal(1, Checks(moves));
    }

    [Fact]
    public void Case_10_an_arrival_where_there_is_no_lot_creates_none()
    {
        // The lot is in W; g arrives in U, where the class has none: no key (U, cls), neither a lot of 0 units nor evidence for one.
        var lots = Run([ClassMove(S, W), StoreItem("g", U)]).Lots;

        Assert.False(lots.ContainsKey((U, "cls")));
        Assert.Equal(1, lots[(W, "cls")], Tolerance);
    }

    // Replays each entry's list with the entry's line, compares the whole law; returns the number of source probabilities compared.
    private static int Checks(MoveEvidence[] moves)
    {
        var checks = 0;
        foreach (var (geid, chain) in Run(moves).Chains)
            foreach (var entry in chain)
            {
                var kept = entry.Evidence.Append(entry.Provenance).ToHashSet();
                var replayed = Run(moves.Where(m => kept.Contains(m.Provenance))).Chains[geid].Single(e => e.Provenance == entry.Provenance);

                foreach (var holder in entry.Predictive.Keys.Union(replayed.Predictive.Keys))
                    Assert.Equal(entry.Predictive.GetValueOrDefault(holder), replayed.Predictive.GetValueOrDefault(holder), 1e-12);
                Assert.Equal(entry.SourceProbability.HasValue, replayed.SourceProbability.HasValue);
                if (entry.SourceProbability is not { } p) continue;
                Assert.Equal(p, replayed.SourceProbability!.Value, 1e-12);
                checks++;
            }
        return checks;
    }
}
