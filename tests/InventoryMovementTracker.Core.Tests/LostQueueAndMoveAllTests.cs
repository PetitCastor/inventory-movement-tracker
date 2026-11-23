using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class LostQueueAndMoveAllTests
{
    private const double Eps = 0.01, Tolerance = 1e-12;
    private static readonly Holder Hand = new("Invalid", ""), S = new("Location", "S"), T = new("Container", "t"), U = new("Location", "U"), A = new("Container", "a");
    private long _line;

    private MoveEvidence Move(string type, string? geid, Holder source, Holder target, long units = 1, string? result = "succeed", long session = 1) =>
        new(new Provenance(session, ++_line, 0), $"2026-11-01T10:00:{_line:00}.0000000Z", type, "cls", geid, units,
            source.Kind, source.Key, target.Kind, target.Key, result, null, null);

    private MoveEvidence Store(string geid, Holder target) => Move("Store", geid, Hand, target);

    private MoveEvidence Buy(long units) => Move("Buy", null, new("Shop", ""), S, units);

    private MoveEvidence Lost(Holder source, Holder target, long units = 1, long session = 1) =>
        Move("Move", null, source, target, units, "lost", session);

    private MoveEvidence LostNamed(string geid, Holder source, Holder target) => Move("Interaction", geid, source, target, result: "lost");

    private MoveEvidence MoveAll(Holder source, Holder target, string? result = "succeed") => Move("MoveAll", null, source, target, 0, result);

    private static (IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Chains, double MeanShortfall, IReadOnlyDictionary<(Holder, string), double> Lots) Run(
        double epsilon, params MoveEvidence[] moves) => NamedChains.Run(moves, [], epsilon, delta: .01);

    private static double Lot(Holder holder, params MoveEvidence[] moves) => Run(Eps, moves).Lots.GetValueOrDefault((holder, "cls"));

    [Fact]
    public void Case_01_lost_moves_do_not_add_to_a_lot_that_covers_them() =>
        Assert.Equal(5, Lot(S, Buy(5), Lost(S, T), Lost(S, T)), Tolerance);

    [Fact]
    public void Case_02_lost_moves_prove_the_units_they_asked_for()
    {
        MoveEvidence[] moves = [Lost(S, T), Lost(S, T)];

        Assert.Equal(2, Lot(S, moves), Tolerance);
        Assert.Equal(0, Lot(T, moves), Tolerance);
    }

    [Fact]
    public void Case_03_a_later_entry_takes_nothing_from_what_already_left() =>
        Assert.Equal(3, Lot(S, Lost(S, T, 3), Lost(U, S, 3)), Tolerance);

    [Fact]
    public void The_largest_need_of_the_queue_counts_not_the_last()
    {
        MoveEvidence[] moves = [Lost(S, T, 3), Lost(U, S, 3), Lost(S, A)];

        Assert.Equal(3, Lot(S, moves), Tolerance);
        Assert.Equal(3, Lot(U, moves), Tolerance);
    }

    [Fact]
    public void An_entry_counts_all_its_units()
    {
        MoveEvidence[] moves = [Lost(U, S, 3), Lost(S, T, 3)];

        Assert.Equal(0, Lot(S, moves), Tolerance);
        Assert.Equal(3, Lot(U, moves), Tolerance);
    }

    [Fact]
    public void Case_04_a_unit_that_entered_in_the_queue_does_not_count_at_its_target()
    {
        MoveEvidence[] moves = [Lost(S, T), Lost(T, S)];

        Assert.Equal(1, Lot(S, moves), Tolerance);
        Assert.Equal(0, Lot(T, moves), Tolerance);
    }

    [Fact]
    public void Case_05_an_unknown_lost_geid_is_one_of_the_units_the_queue_proves()
    {
        var (chains, _, lots) = Run(Eps, LostNamed("g", S, T), Lost(S, T));

        Assert.Equal(1, chains["g"][0].Predictive[S], Tolerance);
        Assert.Equal(1, lots.GetValueOrDefault((S, "cls")), Tolerance);
    }

    [Theory]
    [InlineData("Stack", "succeed", 1)]
    [InlineData("Move", "failed", 1)]
    [InlineData("Stack", null, 2)]   // no answer
    [InlineData(null, null, 2)]
    public void Case_06_any_answer_closes_the_queue(string? type, string? result, double expected) =>
        Assert.Equal(expected, Lot(S, [Lost(S, T), .. type is null ? [] : new[] { Move(type, null, S, S, 1, result) }, Lost(S, T)]), Tolerance);

    [Fact]
    public void Case_07_another_session_closes_the_queue() =>
        Assert.Equal(1, Lot(S, Lost(S, T, session: 1), Lost(S, T, session: 2)), Tolerance);

    [Fact]
    public void Case_08_the_geids_at_the_source_cover_part_of_what_the_queue_proves() =>
        Assert.Equal(.5, Run(.5, Store("g", S), Lost(S, T)).Lots.GetValueOrDefault((S, "cls")), Tolerance);

    [Fact]
    public void Case_09_a_lost_geid_does_not_weigh_a_source_that_the_queue_may_have_filled()
    {
        var chain = Run(Eps, LostNamed("g", S, T), LostNamed("g", T, U)).Chains["g"];

        Assert.Equal(2, chain.Count);
        Assert.Null(chain[1].SourceProbability);
    }

    [Fact]
    public void Case_10_the_queue_bounds_the_lot_before_the_moves_it_spans()
    {
        var lots = Run(Eps, Buy(1), Lost(S, T), Move("Move", null, S, U, result: null), Lost(S, T)).Lots;

        Assert.Equal(2, lots.GetValueOrDefault((S, "cls")) + lots.GetValueOrDefault((U, "cls")), Tolerance);
    }

    [Fact]
    public void Case_11_a_move_all_takes_the_geids_of_its_source()
    {
        var law = Run(Eps, Store("g", S), MoveAll(S, T)).Chains["g"][^1].Smoothed;

        Assert.Equal(.9801, law.GetValueOrDefault(T), Tolerance);
        Assert.Equal(.0099, law[S], Tolerance);
        Assert.Equal(.01, law[Holder.OutOfSight], Tolerance);
    }

    [Fact]
    public void The_chance_to_stay_is_a_parameter_of_the_cut_run_too()
    {
        var chains = ResetCut.Run([Store("g", S), MoveAll(S, T)], [], [], Eps, .01, epsCap: .5).Chains;

        Assert.Equal(.495, chains["g"][^1].Smoothed[T], Tolerance);
    }

    [Fact]
    public void Case_12a_a_move_all_leaves_its_own_target()
    {
        var chains = Run(Eps, Store("t", S), Store("g", S), MoveAll(S, T)).Chains;

        Assert.Equal(.99, chains["t"][^1].Smoothed[S], Tolerance);
        Assert.Equal(.9801, chains["g"][^1].Smoothed.GetValueOrDefault(T), Tolerance);
    }

    [Fact]
    public void Case_12b_a_move_all_leaves_the_container_of_its_target()
    {
        var chains = Run(Eps, Store("t", A), Store("a", S), Store("g", S), MoveAll(S, T)).Chains;

        Assert.Equal(.99, chains["a"][^1].Smoothed[S], Tolerance);
        Assert.Equal(.9801, chains["g"][^1].Smoothed.GetValueOrDefault(T), Tolerance);
    }

    [Fact]
    public void Case_12c_a_cycle_of_containers_ends()
    {
        var chains = Run(Eps, Store("t", A), Store("a", T), Store("g", S), MoveAll(S, T)).Chains;

        Assert.Equal(.9801, chains["g"][^1].Smoothed.GetValueOrDefault(T), Tolerance);
    }

    [Theory]
    [InlineData("succeed")]
    [InlineData(null)]
    public void Case_13_a_move_all_thins_the_lot_of_its_source(string? result)
    {
        MoveEvidence[] moves = [Buy(2), MoveAll(S, T, result)];

        Assert.Equal(.02, Lot(S, moves), Tolerance);
        Assert.Equal(1.98, Lot(T, moves), Tolerance);
        Assert.Equal(.9801, Lot(T, [.. moves, Move("Move", null, T, U)]), Tolerance);   // P(L = 2)
    }
}
