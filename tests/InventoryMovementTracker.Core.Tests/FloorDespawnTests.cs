using Xunit;

namespace InventoryMovementTracker.Core.Tests;

// E8: on the floor, an object stays with S(a + Δ) / S(a), S(x) = w + (1 − w)·exp(−λ·x), λ = 1 h⁻¹ in every test, w = 0.5 unless said.
// a is the age at the geid's previous step, counted from its last arrival at that floor; Δ the hours since that step.
public sealed class FloorDespawnTests
{
    private const double Tolerance = 1e-9;
    private static readonly Holder W1 = new("World", "w1"), W2 = new("World", "w2"), X = new("Location", "X"), Y = new("Location", "Y"),
        B = new("Container", "B"), Hand = new("Invalid", "");
    private long _line;

    private static string At(double hours) => new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero).AddHours(hours).ToString("O");

    private static double S(double x, double w = 0.5) => w + (1 - w) * Math.Exp(-x);

    private static Hazards Floor(double hangar = 0.5) => new(0, 1, 0, 0.9, hangar);

    private MoveEvidence Move(double hours, string type, Holder source, Holder target, string cls = "Tool_x", string? geid = "g", string? result = "succeed") =>
        new(new Provenance(1, ++_line, 0), At(hours), type, cls, geid, 1, source.Kind, source.Key, target.Kind, target.Key, result, null, null);

    private MoveEvidence Drop(double hours, Holder floor, string? result = "succeed", string cls = "Tool_x", string geid = "g") =>
        Move(hours, "Drop", Hand, floor, cls, geid, result);

    private MoveEvidence Proof(double hours, Holder from, string cls = "Tool_x") => Move(hours, "Interaction", from, Holder.Body, cls);

    // A move by class elsewhere: it gives g a step (with ε > 0, g has a crumb Out of sight) and takes nothing from the floor.
    private MoveEvidence Crumb(double hours) => Move(hours, "Move", X, Y, geid: null);

    private static IReadOnlyList<ChainEntry> Run(IEnumerable<MoveEvidence> moves, Hazards? hazards, double eps = 0, IReadOnlyList<DateTimeOffset>? updates = null) =>
        NamedChains.Run(moves, [], eps, eps, hazards, updates).Chains["g"];

    private static void AssertFinite(ChainEntry entry)
    {
        Assert.All(entry.Predictive.Values, p => Assert.True(double.IsFinite(p)));
        Assert.All(entry.Smoothed.Values, p => Assert.True(double.IsFinite(p)));
    }

    [Fact]
    public void Case_01_a_drop_then_a_proof_an_hour_later()
    {
        // a = 0, Δ = 1: S(1) / S(0) = 0.5 + 0.5·e^−1 = 0.683939720585721.
        var chain = Run([Drop(0, W1), Proof(1, W1)], Floor());

        Assert.Equal(0.5 + 0.5 * Math.Exp(-1), chain[1].Predictive[W1], Tolerance);
    }

    [Fact]
    public void Case_02_steps_in_between_that_leave_the_floor_alone_change_nothing()
    {
        // ε = 0.1 so that g, with 0.1 Out of sight, takes part in the two class moves at 1 and 3, which give it steps and no jump to the floor.
        // The steps are at ages 0 (Δ = 1), 1 (Δ = 2) and 3 (Δ = 3): S(1)/S(0) · S(3)/S(1) · S(6)/S(3) = S(6), times 1 − ε = 0.9 for the drop:
        // 0.9·(0.5 + 0.5·e^−6) = 0.9 · 0.50123937608833 = 0.45111543847950.
        var with = Run([Drop(0, W1), Crumb(1), Crumb(3), Proof(6, W1)], Floor(), 0.1);
        var without = Run([Drop(0, W1), Proof(6, W1)], Floor(), 0.1);

        Assert.Equal(4, with.Count);
        Assert.Equal(0.9 * S(6), with[3].Predictive[W1], Tolerance);
        Assert.Equal(0.9 * S(6), without[1].Predictive[W1], Tolerance);
    }

    [Fact]
    public void Case_03_a_long_absence_leaves_the_hangar_share()
    {
        // S(1000) = 0.5 + 0.5·e^−1000 = 0.5 in double precision; w = 0 gives 0.
        var kept = Run([Drop(0, W1), Proof(1000, W1)], Floor())[1];
        var gone = Run([Drop(0, W1), Proof(1000, W1)], Floor(0))[1];

        Assert.Equal(0.5, kept.Predictive[W1], Tolerance);
        AssertFinite(kept);
        Assert.Equal(0, gone.Predictive[W1], Tolerance);
        AssertFinite(gone);
    }

    [Fact]
    public void Case_03b_a_step_after_more_than_745_hours_on_the_floor_is_no_zero_over_zero()
    {
        // Steps at 0 (drop), 800 and 1000: the last one is at age 800, where exp(−800) = 0 and S(1000)/S(800) is 0/0 for w = 0.
        // w = 0: 0.9·0 = 0. w = 0.5: 0.9 · S(800) · S(1000)/S(800) = 0.9·0.5 = 0.45.
        var none = Run([Drop(0, W1), Crumb(800), Proof(1000, W1)], Floor(0), 0.1)[2];
        var half = Run([Drop(0, W1), Crumb(800), Proof(1000, W1)], Floor(), 0.1)[2];

        Assert.Equal(0, none.Predictive[W1], Tolerance);
        AssertFinite(none);
        Assert.Equal(0.45, half.Predictive[W1], Tolerance);
        AssertFinite(half);
    }

    [Fact]
    public void Case_04_without_a_hangar_the_floor_decays_as_before_and_with_all_of_it_nothing_goes()
    {
        // w = 0: e^−λΔ = e^−2 = 0.135335283236613. w = 1: S = 1, the ratio is 1.
        var gone = Run([Drop(0, W1), Proof(2, W1)], Floor(0));
        var kept = Run([Drop(0, W1), Proof(2, W1)], Floor(1));

        Assert.Equal(Math.Exp(-2), gone[1].Predictive[W1], Tolerance);
        Assert.Equal(1, kept[1].Predictive[W1], Tolerance);
    }

    [Fact]
    public void Case_05_a_second_drop_in_the_same_place_restarts_the_age()
    {
        // ε = 0: the second drop at 10 puts all of g on the floor again, with age 0: at 11, S(1) = 0.683939720585721, not S(11)/S(10) ≈ 1.
        var chain = Run([Drop(0, W1), Drop(10, W1), Proof(11, W1)], Floor());

        Assert.Equal(S(1), chain[2].Predictive[W1], Tolerance);
    }

    [Fact]
    public void Case_06_two_floors_each_have_their_age()
    {
        // ε = 0.1. Drop on w1 at 0: w1 = 0.9. At 10: D gives w1 0.9·S(10), then the drop on w2 leaves 0.1 of that on w1 and puts 0.9 on w2.
        // At 11, w1 is at age 10 (since 0) and w2 at age 0 (since 10): w1 = 0.09·S(10)·S(11)/S(10) = 0.09·S(11), w2 = 0.9·S(1).
        var chain = Run([Drop(0, W1), Drop(10, W2), Proof(11, W2)], Floor(), 0.1);

        Assert.Equal(0.09 * S(11), chain[2].Predictive[W1], Tolerance);
        Assert.Equal(0.9 * S(1), chain[2].Predictive[W2], Tolerance);
    }

    [Fact]
    public void Case_07_the_other_holders_keep_their_law()
    {
        // Hazards (1/24, 1, 1/48), ε = 0, no floor involved: a Drink_ in a container is consumed, e^−1 after 24 h; a Drink_ in a station and a tool in
        // a container only leak, e^−0.5. The hangar share w = 0.5 is not theirs.
        var hazards = new Hazards(1.0 / 24, 1, 1.0 / 48);
        MoveEvidence Put(Holder where, string cls) => Move(0, "Interaction", Hand, where, cls);

        Assert.Equal(Math.Exp(-1), Run([Put(B, "Drink_x"), Proof(24, B, "Drink_x")], hazards)[1].Predictive[B], Tolerance);
        Assert.Equal(Math.Exp(-0.5), Run([Put(X, "Drink_x"), Proof(24, X, "Drink_x")], hazards)[1].Predictive[X], Tolerance);
        Assert.Equal(Math.Exp(-0.5), Run([Put(B, "Tool_x"), Proof(24, B)], hazards)[1].Predictive[B], Tolerance);
    }

    [Fact]
    public void Case_07b_a_consumable_on_the_floor_follows_the_despawn()
    {
        // Consumption 1/24 is not used on the floor: S(1) as for any object.
        var chain = Run([Drop(0, W1, cls: "Drink_x"), Proof(1, W1, "Drink_x")], new Hazards(1.0 / 24, 1, 0));

        Assert.Equal(S(1), chain[1].Predictive[W1], Tolerance);
    }

    [Fact]
    public void Case_08_an_update_without_a_reset_multiplies_by_rho()
    {
        // ε = 0.1, ρ' = 0.9, an update at 3. Steps at 0, 2 (age 0, no update in ]0, 2]) and 5 (age 2, one update in ]2, 5]):
        // 0.9 · S(2) · S(5)/S(2) · 0.9 = 0.81·S(5) = 0.81 · 0.50336897349954 = 0.40772886853463.
        var chain = Run([Drop(0, W1), Crumb(2), Proof(5, W1)], Floor(), 0.1, [DateTimeOffset.Parse(At(3))]);

        Assert.Equal(0.81 * S(5), chain[2].Predictive[W1], Tolerance);
    }

    [Fact]
    public void Case_09_without_hazards_nothing_changes()
    {
        MoveEvidence[] moves = [Drop(0, W1), Crumb(2), Proof(1000, W1)];

        // ε = 0.1: the drop puts 0.9 on the floor, and with no hazards nothing leaves it, 2 h or 1 000 h later.
        var chain = Run(moves, null, 0.1);

        Assert.Equal(3, chain.Count);
        Assert.Equal(0.9, chain[1].Predictive[W1], Tolerance);
        Assert.Equal(0.9, chain[2].Predictive[W1], Tolerance);
    }

    [Fact]
    public void Case_12_a_drop_with_a_weighed_source_is_an_arrival()
    {
        // ε = 0.1, no leak. g enters B at 0 (B 0.9, Out of sight 0.1); the drop B → w1 at 2 weighs B: on w1 0.9·0.9·0.9 + 0.1·0.1·0.9 = 0.738, on B 0.081,
        // Out of sight 0.001, total 0.82, so w1 = 0.9. Arrival at 2; steps at 3 (a = 0, Δ = 1), 5 (a = 1, Δ = 2) and 9 (a = 3, Δ = 4): 0.9·S(7).
        var chain = Run([Move(0, "Interaction", Hand, B), Move(2, "Drop", B, W1), Crumb(3), Crumb(5), Proof(9, W1)], Floor(), 0.1);

        Assert.Equal(5, chain.Count);
        Assert.Equal(0.9 * S(7), chain[4].Predictive[W1], Tolerance);
    }

    [Fact]
    public void Case_13_a_crumb_on_the_floor_without_an_arrival_is_at_age_zero_at_each_step()
    {
        // The limit declared: g enters B at 0; the class move at 1 leaves a crumb on w1 (from Out of sight, so no arrival); the class moves at 3 and 7
        // leave it alone. Its age stays 0: its mass at 3 is c·S(2), at 7 c·S(2)·S(4), c being the crumb, which a run with w = 1 (nothing leaves) gives.
        MoveEvidence[] moves = [Move(0, "Interaction", Hand, B), Move(1, "Move", X, W1, geid: null), Crumb(3), Crumb(7)];

        var chain = Run(moves, Floor(), 0.1);
        var crumb = Run(moves, Floor(1), 0.1);

        Assert.Equal(4, chain.Count);
        Assert.True(crumb[2].Predictive[W1] > 1e-6);
        Assert.Equal(S(2), chain[2].Predictive[W1] / crumb[2].Predictive[W1], Tolerance);
        Assert.Equal(S(2) * S(4), chain[3].Predictive[W1] / crumb[3].Predictive[W1], Tolerance);
    }

    [Fact]
    public void Case_14_a_hangar_share_of_one_fifth()
    {
        // w = 0.2, Δ = 2: 0.2 + 0.8·e^−2 = 0.308268226589.
        var chain = Run([Drop(0, W1), Proof(2, W1)], Floor(0.2));

        Assert.Equal(0.2 + 0.8 * Math.Exp(-2), chain[1].Predictive[W1], Tolerance);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("lost")]
    public void Case_10_a_failed_or_lost_drop_is_no_arrival(string result)
    {
        // The arrival stays at 0. failed: no step, so one step of Δ = 11 at age 0, S(11). lost: a step at 10 (age 0, Δ = 10, no jump) and the proof at
        // age 10 (Δ = 1): S(10) · S(11)/S(10) = S(11). An arrival at 10 would give S(10)·S(1) for lost.
        var chain = Run([Drop(0, W1), Drop(10, W1, result), Proof(11, W1)], Floor());

        Assert.Equal(S(11), chain[^1].Predictive[W1], Tolerance);
    }

    [Fact]
    public void Case_11_a_list_replayed_gives_the_same_law_and_without_the_drop_another()
    {
        // g dropped on w1 at 0, h (another class, so that the class moves leave it out) on the same floor at 4, then g's steps at 2, 3 and 7.
        // h's arrival is not g's: g's last step is at age 3 (Δ = 4), and 0.9·S(2) · S(3)/S(2) · S(7)/S(3) = 0.9·S(7). With the arrival kept per floor
        // only, h's at 4 would make g's age max(0, 3 − 4) = 0 and its last factor S(4). g's list has no line of h's, so its replay would disagree too.
        MoveEvidence[] moves = [Drop(0, W1), Crumb(2), Crumb(3), Drop(4, W1, cls: "Other_x", geid: "h"), Proof(7, W1)];
        var chains = NamedChains.Run(moves, [], 0.1, 0.1, Floor()).Chains;

        Assert.Equal(0.9 * S(7), chains["g"][^1].Predictive[W1], Tolerance);
        foreach (var (geid, chain) in chains)
            foreach (var entry in chain)
            {
                var kept = entry.Evidence.Append(entry.Provenance).ToHashSet();
                var replayed = NamedChains.Run(moves.Where(m => kept.Contains(m.Provenance)), [], 0.1, 0.1, Floor()).Chains[geid]
                    .Single(e => e.Provenance == entry.Provenance);
                foreach (var holder in entry.Predictive.Keys.Union(replayed.Predictive.Keys))
                    Assert.Equal(entry.Predictive.GetValueOrDefault(holder), replayed.Predictive.GetValueOrDefault(holder), 1e-12);
            }

        var drop = moves[0].Provenance;
        var last = chains["g"][^1];
        Assert.Contains(drop, last.Evidence);
        var without = NamedChains.Run(moves.Where(m => m.Provenance != drop), [], 0.1, 0.1, Floor()).Chains["g"].Single(e => e.Provenance == last.Provenance);
        Assert.True(Math.Abs(last.Predictive[W1] - without.Predictive.GetValueOrDefault(W1)) > 0.1);
    }
}
