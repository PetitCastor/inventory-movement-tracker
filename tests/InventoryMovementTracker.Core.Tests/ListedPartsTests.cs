using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class ListedPartsTests
{
    private const double Eps = .01, Delta = .02, Tolerance = 1e-9;
    private static readonly Holder Body = Holder.Body, Ck = new("Container", "k"), W = new("Location", "W");
    private long _line;

    private static string At(double seconds) => new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero).AddSeconds(seconds).ToString("O");

    private SightingEvidence Seen(string geid, string port, double seconds, long burst, string? parent = null, string cls = "cls", bool placeholder = false) =>
        new(new Provenance(1, ++_line, 0), At(seconds), geid, cls, port, burst, parent, placeholder);

    // p seen in the container k, in its own burst.
    private SightingEvidence OnK(string geid, double seconds, long burst = 1, string parent = "k") => Seen(geid, "main", seconds, burst, parent);

    private MoveEvidence Move(string type, string? geid, Holder source, Holder target, double seconds, long units, string result) =>
        new(new Provenance(1, ++_line, 0), At(seconds), type, "cls", geid, units, source.Kind, source.Key, target.Kind, target.Key, result, null, null);

    private MoveEvidence Take(string geid, Holder source, Holder target, double seconds, string result = "succeed") =>
        Move("Interaction", geid, source, target, seconds, 1, result);

    private MoveEvidence ClassMove(Holder source, Holder target, double seconds) => Move("Move", null, source, target, seconds, 1, "succeed");

    // A complete listing: the Body_ItemPort line at `seconds`, then one line on the body per listed geid.
    private SightingEvidence[] Listing(long burst, double seconds, params string[] listed) =>
        [Seen("suit", "Body_ItemPort", seconds, burst, cls: "suit"), .. listed.Select((g, i) => Seen(g, "main", seconds + 1 + i, burst, cls: "box"))];

    private static IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Run(IEnumerable<MoveEvidence> moves, IEnumerable<SightingEvidence> seen, double eps = Eps) =>
        NamedChains.Run(moves, seen, eps, Delta).Chains;

    // Replays each entry's list with the entry's line and compares the whole predictive law.
    private static void Sufficient(MoveEvidence[] moves, SightingEvidence[] seen, double eps = Eps)
    {
        foreach (var (geid, chain) in Run(moves, seen, eps))
            foreach (var entry in chain)
            {
                var kept = entry.Evidence.Append(entry.Provenance).ToHashSet();
                var replayed = Run(moves.Where(m => kept.Contains(m.Provenance)), seen.Where(s => kept.Contains(s.Provenance)), eps)[geid]
                    .Single(e => e.Provenance == entry.Provenance);

                foreach (var holder in entry.Predictive.Keys.Union(replayed.Predictive.Keys))
                    Assert.Equal(entry.Predictive.GetValueOrDefault(holder), replayed.Predictive.GetValueOrDefault(holder), 1e-12);
            }
    }

    [Fact]
    public void Case_01_a_part_of_a_listed_container_that_the_listing_omits_goes_out_of_sight_with_one_minus_delta()
    {
        // p seen on k: Out of sight .01, k .99 (ε = .01). A listing with k and without p sends 1 − δ = .98 of k's mass:
        // k .99 · .02 = .0198, Out of sight .01 + .99 · .98 = .9802. A second listing: k .0198 · .02 = .000396.
        SightingEvidence[] one = [OnK("p", 0), .. Listing(2, 60, "k")];
        var first = Run([], one)["p"];

        Assert.Equal(2, first.Count);
        Assert.Equal(one[1].Provenance, first[1].Provenance);
        Assert.Equal(.99, first[1].Predictive[Ck], Tolerance);
        Assert.Equal(.0198, first[1].Smoothed[Ck], Tolerance);
        Assert.Equal(.9802, first[1].Smoothed[Holder.OutOfSight], Tolerance);

        SightingEvidence[] two = [OnK("p", 0), .. Listing(2, 60, "k"), .. Listing(3, 120, "k")];
        var second = Run([], two)["p"];

        Assert.Equal(3, second.Count);
        Assert.Equal(.000396, second[2].Smoothed[Ck], Tolerance);
        Assert.Equal(.999604, second[2].Smoothed[Holder.OutOfSight], Tolerance);
    }

    [Fact]
    public void Case_02_a_listing_without_the_container_leaves_the_part_alone()
    {
        // k is known, seen in burst 1, but the listing (burst 2) does not have it: p keeps k .99 and has no second entry.
        SightingEvidence[] seen = [Seen("k", "main", 0, 1, cls: "box"), OnK("p", 1), Seen("suit", "Body_ItemPort", 60, 2, cls: "suit")];

        var chain = Run([], seen)["p"];

        Assert.Single(chain);
        Assert.Equal(.99, chain[0].Smoothed[Ck], Tolerance);
    }

    [Theory]
    [InlineData("k", .9999)]
    [InlineData("o", .0099)]
    [InlineData(null, .0099)]
    public void Case_03_a_part_the_listing_has_is_not_evicted_only_jumped(string? parent, double kMass)
    {
        // p on k (Out of sight .01, k .99), then listed on k, on another container o or on the body. The jump alone: on k, k .99 + .01 · .99
        // = .9999 and Out of sight .0001; elsewhere k keeps ε of .99 = .0099 and the new holder has .99. An eviction would take 98 % of k first.
        SightingEvidence[] seen = [OnK("p", 0), .. Listing(2, 60, "k"), Seen("p", "main", 62, 2, parent)];

        var chain = Run([], seen)["p"];

        Assert.Equal(2, chain.Count);
        Assert.Equal(kMass, chain[1].Smoothed[Ck], Tolerance);
        Holder here = parent == "k" ? Holder.OutOfSight : parent is null ? Body : new Holder("Container", parent);
        Assert.Equal(parent == "k" ? .0001 : .99, chain[1].Smoothed[here], Tolerance);
    }

    [Fact]
    public void Case_04_a_part_stored_in_a_listed_container_by_a_move_is_not_evaluated()
    {
        // p goes from W to k by a named move, first proof: Out of sight .01, k .99 after the normalisation. The listing has k, not p: nothing.
        MoveEvidence[] moves = [Take("p", W, Ck, 0)];

        var chain = Run(moves, Listing(2, 60, "k"))["p"];

        Assert.Single(chain);
        Assert.Equal(.99, chain[0].Smoothed[Ck], Tolerance);
    }

    [Fact]
    public void Case_04b_a_part_seen_on_k_then_stored_in_k_by_a_move_is_not_evaluated()
    {
        // Sight on k: Out of sight .01, k .99. The move W → k weighs a source W that p never held, ε = .01 on each state: Out of sight
        // .01 · .01 · .01 = 1e-6, k .01 · .01 · .99 + .99 · .01 = .009999; normalised by .01: k .9999.
        MoveEvidence[] moves = [Take("p", W, Ck, 30)];

        var chain = Run(moves, [OnK("p", 0), .. Listing(2, 60, "k")])["p"];

        Assert.Equal(2, chain.Count);
        Assert.Equal(.9999, chain[1].Smoothed[Ck], Tolerance);
    }

    [Fact]
    public void Case_05_a_part_moved_elsewhere_by_a_named_move_is_not_evaluated_until_seen_on_k_again()
    {
        // Sight on k: [Out of sight .01, k .99, W 0]. The move k → W: Out of sight 1e-6, k .99 · .99 · .01 = .009801, W .970398, over .9802.
        // Not evaluated: k keeps .009801 / .9802, no third entry.
        MoveEvidence[] moves = [Take("p", Ck, W, 30)];
        var away = Run(moves, [OnK("p", 0), .. Listing(2, 60, "k")])["p"];

        Assert.Equal(2, away.Count);
        Assert.Equal(.009801 / .9802, away[1].Smoothed[Ck], Tolerance);

        // ε = 0: sight on k (k 1), move to W (W 1), sight on k again (k 1), listing: k 1 · .02 = .02, Out of sight .98.
        SightingEvidence[] seen = [OnK("p", 0), OnK("p", 60, 2), .. Listing(3, 120, "k")];
        var back = Run(moves, seen, eps: 0)["p"];

        Assert.Equal(4, back.Count);
        Assert.Equal(.02, back[3].Smoothed[Ck], Tolerance);
        Assert.Equal(.98, back[3].Smoothed[Holder.OutOfSight], Tolerance);
    }

    [Fact]
    public void Case_05b_a_part_seen_on_another_container_later_is_a_part_of_that_one()
    {
        // ε = 0: p on k, then on j (j 1). The listing has k and not j: nothing. With j listed and not k: j 1 · .02 = .02.
        SightingEvidence[] seen = [OnK("p", 0), OnK("p", 30, 1, "j")];
        var onlyK = Run([], [.. seen, .. Listing(2, 60, "k")], eps: 0)["p"];
        var onlyJ = Run([], [.. seen, .. Listing(2, 60, "j")], eps: 0)["p"];

        Assert.Equal(2, onlyK.Count);
        Assert.Equal(3, onlyJ.Count);
        Assert.Equal(.02, onlyJ[2].Smoothed[new("Container", "j")], Tolerance);
    }

    [Fact]
    public void Case_06_a_lost_move_between_the_sight_and_the_listing_leaves_the_geid_a_part()
    {
        // ε = 0: sight on k (k 1); the lost move from k keeps k (source weighed, 1); the listing: k .02, Out of sight .98.
        MoveEvidence[] moves = [Take("p", Ck, W, 30, "lost")];

        var chain = Run(moves, [OnK("p", 0), .. Listing(2, 60, "k")], eps: 0)["p"];

        Assert.Equal(3, chain.Count);
        Assert.Equal(.02, chain[2].Smoothed[Ck], Tolerance);
        Assert.Equal(.98, chain[2].Smoothed[Holder.OutOfSight], Tolerance);
    }

    [Fact]
    public void Case_07_a_geid_on_the_body_and_on_k_takes_one_step_for_both_masses()
    {
        // Sight on the body: [Out of sight .01, body .99]. Sight on k: Out of sight .0001, body .99 · .01 = .0099, k .01 · .99 + .99 · .99 = .99.
        // One listing step with k: body .0099 · .02 = .000198, k .99 · .02 = .0198, Out of sight 1 − .020 = .980002. Three entries, not four.
        SightingEvidence[] seen = [Seen("p", "main", 0, 1), OnK("p", 1), .. Listing(2, 60, "k")];

        var chain = Run([], seen)["p"];

        Assert.Equal(3, chain.Count);
        Assert.Equal(.000198, chain[2].Smoothed[Body], Tolerance);
        Assert.Equal(.0198, chain[2].Smoothed[Ck], Tolerance);
        Assert.Equal(.980002, chain[2].Smoothed[Holder.OutOfSight], Tolerance);
        Sufficient([], [.. seen]);
    }

    [Fact]
    public void Case_08_a_class_move_that_took_a_share_of_the_geid_leaves_only_what_stayed_in_k_to_evaluate()
    {
        // p then q seen on k, each k .99. The class move of one unit from k serves q first (later arrival: r = 1), then p with
        // r = P(q absent) = 1 − .99 = .01: p keeps k .99 · .99 = .9801. The listing then takes 98 %: .9801 · .02 = .019602.
        SightingEvidence[] seen = [OnK("p", 0), OnK("q", 1), .. Listing(2, 60, "k")];

        var chain = Run([ClassMove(Ck, W, 30)], seen)["p"];

        Assert.Equal(3, chain.Count);
        Assert.Equal(.9801, chain[2].Predictive[Ck], Tolerance);
        Assert.Equal(.019602, chain[2].Smoothed[Ck], Tolerance);
    }

    [Fact]
    public void Case_08b_a_move_all_on_the_parent_leaves_the_piece_state_alone()
    {
        // Sight on k: Out of sight .01, k .99. The MoveAll k -> W keeps 1 % of k (epsCap .01): k .0099, W .9801. It is no named line of p, so p
        // stays a part of k: the listing multiplies what is left in k by .02: .0099 · .02 = .000198. W keeps .9801.
        SightingEvidence[] seen = [OnK("p", 0), .. Listing(2, 60, "k")];

        var chain = Run([Move("MoveAll", null, Ck, W, 30, 0, "succeed")], seen)["p"];

        Assert.Equal(3, chain.Count);
        Assert.Equal(.0099, chain[2].Predictive[Ck], Tolerance);
        Assert.Equal(.000198, chain[2].Smoothed[Ck], Tolerance);
        Assert.Equal(.9801, chain[2].Smoothed[W], Tolerance);
    }

    [Fact]
    public void Case_09_the_part_of_a_part_is_not_evaluated_through_the_grandparent()
    {
        // The lamp is on the scope, the scope on the rifle. The listing has the rifle only: the scope's mass on the rifle goes from .99 to
        // .99 · .02 = .0198; the lamp's parent, the scope, is not listed, so the lamp keeps .99 on the scope.
        SightingEvidence[] seen = [OnK("scope", 0, 1, "rifle"), OnK("lamp", 1, 1, "scope"), .. Listing(2, 60, "rifle")];

        var chains = Run([], seen);

        Assert.Equal(2, chains["scope"].Count);
        Assert.Equal(.0198, chains["scope"][1].Smoothed[new("Container", "rifle")], Tolerance);
        Assert.Single(chains["lamp"]);
        Assert.Equal(.99, chains["lamp"][0].Smoothed[new("Container", "scope")], Tolerance);
    }

    [Fact]
    public void Case_10_an_incomplete_burst_or_a_placeholder_line_of_k_does_not_list_k()
    {
        // No Body_ItemPort line: nothing. This half pins the trigger (no listing event without Body_ItemPort) and cannot fail through the new code.
        var incomplete = Run([], [OnK("p", 0), Seen("k", "main", 60, 2, cls: "box")])["p"];

        Assert.Single(incomplete);
        Assert.Equal(.99, incomplete[0].Smoothed[Ck], Tolerance);

        // A complete burst whose only line of k is a placeholder: k is not listed.
        var placeholder = Run([], [OnK("p", 0), Seen("suit", "Body_ItemPort", 60, 2, cls: "suit"), Seen("k", "main", 61, 2, cls: "box", placeholder: true)])["p"];

        Assert.Single(placeholder);
        Assert.Equal(.99, placeholder[0].Smoothed[Ck], Tolerance);
    }

    [Fact]
    public void Case_11_the_list_of_a_later_entry_carries_the_listing_line_and_the_line_of_k()
    {
        // After the listing p is at k .0198; the move out of k at 120 s weighs that source. Its list needs the sight, the Body_ItemPort line
        // and the first line of k: without the first, k is not listed in the replay; without the second, there is no listing.
        MoveEvidence[] moves = [Take("p", Ck, W, 120)];
        SightingEvidence[] seen = [OnK("p", 0), .. Listing(2, 60, "k")];

        var chain = Run(moves, seen)["p"];

        Assert.Equal(3, chain.Count);
        Assert.Equal(.0198, chain[2].Predictive[Ck], Tolerance);
        Assert.Contains(seen[1].Provenance, chain[2].Evidence);
        Assert.Contains(seen[2].Provenance, chain[2].Evidence);
        Sufficient(moves, seen);
    }

    [Fact]
    public void Case_11b_the_list_carries_the_first_line_of_k_of_the_burst_and_not_the_second()
    {
        // k has two lines in the listing burst (61 s, 62 s): the list of the move at 120 s has the Body_ItemPort line and the first of them.
        MoveEvidence[] moves = [Take("p", Ck, W, 120)];
        SightingEvidence[] seen = [OnK("p", 0), .. Listing(2, 60, "k", "k")];

        var chain = Run(moves, seen)["p"];

        Assert.Equal(3, chain.Count);
        Assert.Contains(seen[1].Provenance, chain[2].Evidence);
        Assert.Contains(seen[2].Provenance, chain[2].Evidence);
        Assert.DoesNotContain(seen[3].Provenance, chain[2].Evidence);
        Sufficient(moves, seen);
    }

    [Fact]
    public void Case_11c_a_part_with_no_mass_in_k_does_not_read_the_line_of_k()
    {
        // ε = 0: sight on k (k 1); a class move of one unit from k to the body takes all of p (r = 1): body 1, k exactly 0. Still a part of k
        // (no named line since), k is listed, but only the body row has mass: body 1 · .02 = .02, and k's line is not in the list.
        SightingEvidence[] seen = [OnK("p", 0), .. Listing(2, 60, "k")];

        var chain = Run([ClassMove(Ck, Body, 30)], seen, eps: 0)["p"];

        Assert.Equal(3, chain.Count);
        Assert.Equal(1, chain[2].Predictive[Body], Tolerance);
        Assert.Equal(.02, chain[2].Smoothed[Body], Tolerance);
        Assert.Contains(seen[1].Provenance, chain[2].Evidence.Append(chain[2].Provenance));
        Assert.DoesNotContain(seen[2].Provenance, chain[2].Evidence);
    }
}
