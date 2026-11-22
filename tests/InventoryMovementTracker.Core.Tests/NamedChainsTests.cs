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

    private static IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Run(IEnumerable<MoveEvidence> moves, IEnumerable<SightingEvidence> sightings) =>
        NamedChains.Run(moves, sightings, epsilon: Eps, delta: Eps);

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
