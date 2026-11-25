using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class StackMoveTests
{
    private const double Eps = 0.01, Tolerance = 1e-12;
    private static readonly Holder Hand = new("Invalid", ""), Shop = new("Shop", ""), S = new("Location", "S"), T = new("Container", "t");
    private long _line;

    private MoveEvidence Move(string type, string? geid, Holder source, Holder target, long units = 1, string? result = "succeed", string cls = "cls") =>
        new(new Provenance(1, ++_line, 0), $"2026-11-01T10:00:{_line:00}.0000000Z", type, cls, geid, units,
            source.Kind, source.Key, target.Kind, target.Key, result, null, null);

    private MoveEvidence Buy(long units, string cls = "cls") => Move("Buy", null, Shop, S, units, cls: cls);

    private MoveEvidence Stack(Holder source, Holder target, long units = 2, string? result = "succeed", string? geid = null) =>
        Move("Stack", geid, source, target, units, result);

    private MoveEvidence Store(string geid, Holder target) => Move("Store", geid, Hand, target);

    private static (IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Chains, double MeanShortfall, IReadOnlyDictionary<(Holder, string), double> Lots) Run(
        params MoveEvidence[] moves) => NamedChains.Run(moves, [], Eps, delta: .01);

    private static double Lot(Holder holder, params MoveEvidence[] moves) => Run(moves).Lots.GetValueOrDefault((holder, "cls"));

    [Theory]
    [InlineData("succeed")]
    [InlineData(null)]
    public void Case_01_a_stack_between_two_inventories_moves_the_class(string? result)
    {
        MoveEvidence[] moves = [Buy(5), Stack(S, T, result: result)];

        Assert.Equal(3, Lot(S, moves), Tolerance);
        Assert.Equal(2, Lot(T, moves), Tolerance);
    }

    [Fact]
    public void Case_02_a_stack_within_one_inventory_moves_nothing()
    {
        Assert.Equal(5, Lot(S, Buy(5), Stack(S, S)), Tolerance);
        Assert.Equal(0, Lot(S, Stack(S, S, result: "lost")), Tolerance);   // its queue proves nothing

        var chain = Run(Store("g", S), Stack(S, S, 1)).Chains["g"];

        Assert.Single(chain);
    }

    [Fact]
    public void Case_03_a_failed_stack_moves_nothing_and_a_lost_one_is_a_queue()
    {
        MoveEvidence[] failed = [Buy(5), Stack(S, T, result: "failed")];

        Assert.Equal(5, Lot(S, failed), Tolerance);
        Assert.Equal(0, Lot(T, failed), Tolerance);

        MoveEvidence[] lost = [Stack(S, T, result: "lost")];

        Assert.Equal(2, Lot(S, lost), Tolerance);
        Assert.Equal(0, Lot(T, lost), Tolerance);
    }

    [Theory]
    [InlineData("Invalid", "", "Container", "t")]
    [InlineData("Shop", "", "Container", "t")]
    [InlineData("Location", "S", "Invalid", "")]
    [InlineData("Location", "S", "Shop", "")]
    public void Case_04_a_stack_without_a_real_source_or_target_is_ignored(string sourceKind, string sourceKey, string targetKind, string targetKey)
    {
        var (_, _, lots) = Run(Buy(5), Stack(new(sourceKind, sourceKey), new(targetKind, targetKey)));

        Assert.Equal([(S, "cls")], lots.Keys);
        Assert.Equal(5, lots[(S, "cls")], Tolerance);
    }

    [Theory]
    [InlineData("StackAll")]
    [InlineData("Split")]
    [InlineData("Sort")]
    [InlineData("AmmoRepool")]
    [InlineData("QueryInventory")]
    public void Case_05_the_other_moves_that_are_not_by_class_are_still_ignored(string type)
    {
        MoveEvidence[] moves = [Buy(5), Move(type, null, S, T, 2)];

        Assert.Equal(5, Lot(S, moves), Tolerance);
        Assert.Equal(0, Lot(T, moves), Tolerance);
    }

    [Fact]
    public void Case_06_a_stack_with_a_geid_is_ignored()
    {
        var (chains, _, lots) = Run(Buy(5), Store("g", S), Stack(S, T, 1, geid: "g"));

        Assert.Single(chains["g"]);
        Assert.Equal(5, lots[(S, "cls")], Tolerance);
        Assert.False(lots.ContainsKey((T, "cls")));
    }

    [Fact]
    public void Case_07_a_stack_after_a_stored_geid_gives_the_laws_and_lists_of_a_move()
    {
        var stack = Flow("Stack");
        var move = Flow("Move");

        Assert.Equal(2, stack.Chains["g"].Count);
        Assert.Equal(move.Chains["g"].Count, stack.Chains["g"].Count);
        foreach (var (s, m) in stack.Chains["g"].Zip(move.Chains["g"]))
        {
            Assert.Equal(m.Evidence, s.Evidence);
            Assert.Equal(m.Predictive.Keys, s.Predictive.Keys);
            Assert.Equal(m.Smoothed.Keys, s.Smoothed.Keys);
            foreach (var holder in m.Predictive.Keys) Assert.Equal(m.Predictive[holder], s.Predictive[holder], Tolerance);
            foreach (var holder in m.Smoothed.Keys) Assert.Equal(m.Smoothed[holder], s.Smoothed[holder], Tolerance);
        }
        Assert.Equal(move.Lots.Keys, stack.Lots.Keys);
        foreach (var key in move.Lots.Keys) Assert.Equal(move.Lots[key], stack.Lots[key], Tolerance);
    }

    // The same lines in both, so that the provenances are the same too.
    private (IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Chains, double MeanShortfall, IReadOnlyDictionary<(Holder, string), double> Lots) Flow(string type)
    {
        _line = 0;
        return Run(Store("g", S), Move(type, null, S, T, 1));
    }

    [Fact]
    public void Case_08_the_stack_of_a_class_leaves_the_other_class_alone()
    {
        var lots = Run(Buy(5), Buy(4, "other"), Stack(S, T)).Lots;

        Assert.Equal(3, lots[(S, "cls")], Tolerance);
        Assert.Equal(2, lots[(T, "cls")], Tolerance);
        Assert.Equal(4, lots[(S, "other")], Tolerance);
        Assert.False(lots.ContainsKey((T, "other")));
    }

    [Fact]
    public void Case_09_a_stack_of_no_unit_moves_nothing()
    {
        MoveEvidence[] moves = [Buy(5), Stack(S, T, 0)];

        Assert.Equal(5, Lot(S, moves), Tolerance);
        Assert.Equal(0, Lot(T, moves), Tolerance);
    }
}
