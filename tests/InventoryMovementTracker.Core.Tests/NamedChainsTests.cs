using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class NamedChainsTests
{
    private const double Eps = 0.01;
    private static readonly Holder A = new("Location", "A"), B = new("Container", "B"), C = new("Location", "C");
    private long _line;

    private static string At(int second) => $"2026-11-01T10:00:{second:00}.0000000Z";

    private MoveEvidence Move(int second, string geid, Holder source, Holder target, string? result = "succeed", string type = "Store") =>
        new(new Provenance(1, ++_line, 0), At(second), type, "cls", geid, 1, source.Kind, source.Key, target.Kind, target.Key, result, null, null);

    private SightingEvidence Seen(int second, string geid, long burst, string port = "Backpack", string? parent = null) =>
        new(new Provenance(1, ++_line, 0), At(second), geid, "cls", port, burst, parent, false);

    // A move by class: no geid, n units of "cls".
    private MoveEvidence ClassMove(int second, Holder source, Holder target, long units) =>
        new(new Provenance(1, ++_line, 0), At(second), "Move", "cls", null, units, source.Kind, source.Key, target.Kind, target.Key, "succeed", null, null);

    private static readonly Holder Hand = new("Invalid", "");

    private static IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Run(IEnumerable<MoveEvidence> moves, IEnumerable<SightingEvidence> sightings) =>
        NamedChains.Run(moves, sightings, epsilon: Eps, delta: Eps).Chains;

    [Fact]
    public void A_one_unit_class_move_takes_the_latest_arrival()
    {
        var chains = Run([Move(1, "first", Hand, B), Move(2, "second", Hand, B), ClassMove(3, B, C, 1)], []);

        Assert.True(chains["second"][^1].Smoothed[C] > 0.98);
        Assert.True(chains["first"][^1].Smoothed[B] > 0.97);
        Assert.True(chains["first"][^1].Smoothed.GetValueOrDefault(C) < 0.02);
    }

    [Fact]
    public void A_two_unit_class_move_takes_both()
    {
        var chains = Run([Move(1, "first", Hand, B), Move(2, "second", Hand, B), ClassMove(3, B, C, 2)], []);

        Assert.True(chains["second"][^1].Smoothed[C] > 0.98);
        Assert.True(chains["first"][^1].Smoothed[C] > 0.98);
    }

    [Fact]
    public void An_uncertain_candidate_moves_with_its_presence()
    {
        // With ε = 0.5 the Store leaves the geid in B with 0.5 and Out of sight with 0.5.
        var (chains, shortfall, _) = NamedChains.Run([Move(1, "g", Hand, B), ClassMove(2, B, C, 1)], [], epsilon: 0.5, delta: Eps);

        Assert.Equal(0.5, chains["g"][^1].Smoothed[C], 1e-12);
        Assert.Equal(0.5, shortfall, 1e-12);   // the unit came from elsewhere half of the time
    }

    [Fact]
    public void A_class_move_carries_the_geid_to_the_source_of_its_next_named_move()
    {
        // The B4b diagnosis: stored in A, moved by class from A to B, then taken out of B by name.
        var chain = Run([Move(1, "g", Hand, A), ClassMove(2, A, B, 1), Move(3, "g", B, Holder.Body, type: "Interaction")], [])["g"];

        Assert.Equal(3, chain.Count);
        Assert.True(chain[2].SourceProbability > 0.98);
    }

    [Fact]
    public void A_store_from_an_invalid_source_weighs_the_body()
    {
        var chain = Run([Move(1, "g", A, Holder.Body, type: "Interaction"), Move(2, "g", Hand, B)], [])["g"];

        Assert.Equal(Holder.Body, chain[1].Source);
        Assert.Null(chain[1].SourceProbability);   // the body is not a real source: not a check
    }

    [Fact]
    public void A_second_move_predicts_the_first_move_target_as_its_source()
    {
        var chain = Run([Move(1, "g", A, B), Move(2, "g", B, C)], [])["g"];

        Assert.Null(chain[0].SourceProbability);   // no earlier evidence: not a check
        Assert.Equal(1 - Eps, chain[1].SourceProbability!.Value, 1e-12);
        Assert.Equal(B, chain[1].Source);
    }

    [Fact]
    public void A_contradicted_source_is_predicted_low_and_the_jump_still_happens()
    {
        var d = new Holder("Container", "D");

        var chain = Run([Move(1, "g", A, B), Move(2, "g", d, C)], [])["g"];

        Assert.True(chain[1].SourceProbability < 0.01);
        Assert.True(chain[1].Smoothed[C] > 0.98);
    }

    [Fact]
    public void Failed_changes_nothing_and_lost_does_not_jump()
    {
        var failed = Run([Move(1, "g", A, B), Move(2, "g", B, C, result: "failed")], [])["g"];
        Assert.Single(failed);
        Assert.False(failed[0].Smoothed.ContainsKey(C));

        var lost = Run([Move(1, "g", A, B), Move(2, "g", B, C, result: "lost")], [])["g"];
        Assert.Equal(2, lost.Count);
        Assert.True(lost[1].Smoothed[B] > 0.98);
        Assert.True(lost[1].Smoothed.GetValueOrDefault(C) < 0.01);
    }

    [Fact]
    public void A_complete_listing_without_the_worn_geid_puts_it_out_of_sight_until_it_is_seen_again()
    {
        var moves = new[] { Move(1, "g", A, Holder.Body, type: "Interaction") };
        var sightings = new[]
        {
            Seen(2, "body", burst: 100, port: "Body_ItemPort"),
            Seen(2, "other", burst: 100),
            Seen(5, "g", burst: 200, parent: "pack"),
        };

        var chain = Run(moves, sightings)["g"];

        Assert.Equal(3, chain.Count);
        Assert.True(chain[0].Smoothed[Holder.Body] > 0.98);
        Assert.True(chain[1].Smoothed[Holder.OutOfSight] > 0.98);
        Assert.True(chain[2].Smoothed[new Holder("Container", "pack")] > 0.98);
    }

    [Fact]
    public void A_part_with_a_parent_is_held_by_the_parent_container()
    {
        var chains = Run([], [Seen(1, "helmet", burst: 100, port: "Body_ItemPort"), Seen(1, "visor", burst: 100, parent: "helmet")]);

        Assert.True(chains["visor"][0].Smoothed[new Holder("Container", "helmet")] > 0.98);
        Assert.True(chains["helmet"][0].Smoothed[Holder.Body] > 0.98);
    }
}
