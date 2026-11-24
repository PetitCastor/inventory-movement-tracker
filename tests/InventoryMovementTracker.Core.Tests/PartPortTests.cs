using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class PartPortTests
{
    private const double Eps = .01, Delta = .02, Tolerance = 1e-9;
    private static readonly Holder Body = Holder.Body, Ck = new("Container", "k"), Cj = new("Container", "j"), W = new("Location", "W"), U = new("Location", "U");
    private long _line;

    private static string At(double seconds) => new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero).AddSeconds(seconds).ToString("O");

    private SightingEvidence Seen(string geid, string port, double seconds, long burst, string? parent = null, string cls = "cls") =>
        new(new Provenance(1, ++_line, 0), At(seconds), geid, cls, port, burst, parent, false);

    private MoveEvidence Take(string geid, Holder source, Holder target, double seconds) =>
        new(new Provenance(1, ++_line, 0), At(seconds), "Interaction", "cls", geid, 1, source.Kind, source.Key, target.Kind, target.Key, "succeed", null, null);

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

    [Theory]
    [InlineData("magazine_attach_1")]
    [InlineData("main")]
    [InlineData("backpack")]
    [InlineData("")]
    public void Case_01_a_part_taken_over_on_its_port_of_a_parent_loses_its_mass_there_with_one_minus_delta(string port)
    {
        // o seen on k: Out of sight .01, k .99 (ε = .01). g on the same port of k, in another burst, sends 1 − δ = .98 of k:
        // k .99 · .02 = .0198, Out of sight .01 + .99 · .98 = .9802. The order against g's jump is not observable: o and g have laws of their own.
        SightingEvidence old = Seen("o", port, 0, 1, "k"), cur = Seen("g", port, 60, 2, "k");

        var chains = Run([], [old, cur]);

        Assert.Equal(2, chains["o"].Count);
        Assert.Equal(cur.Provenance, chains["o"][1].Provenance);
        Assert.Equal(.99, chains["o"][1].Predictive[Ck], Tolerance);
        Assert.Equal(.0198, chains["o"][1].Smoothed[Ck], Tolerance);
        Assert.Equal(.9802, chains["o"][1].Smoothed[Holder.OutOfSight], Tolerance);
        Assert.Single(chains["g"]);
        Assert.Equal(.99, chains["g"][0].Smoothed[Ck], Tolerance);
    }

    [Fact]
    public void Case_01b_a_part_with_a_small_mass_left_in_k_is_evicted_all_the_same()
    {
        // A sight on k: Out of sight .01, k .99, W 0. The move k to W (a named move keeps the last port (k, p)): Out of sight 1e-6, k .99 · .99 · .01
        // = .009801, W .970398, over .9802: k = .009801 / .9802 = .00999898..., far under a half. The eviction sends 1 − δ of it:
        // k = .02 · .009801 / .9802 = .00019602 / .9802 = 1.99979596e-4.
        SightingEvidence[] seen = [Seen("o", "p", 0, 1, "k"), Seen("g", "p", 60, 2, "k")];

        var chain = Run([Take("o", Ck, W, 30)], seen)["o"];

        Assert.Equal(3, chain.Count);
        Assert.Equal(seen[1].Provenance, chain[2].Provenance);
        Assert.Equal(.009801 / .9802, chain[2].Predictive[Ck], Tolerance);
        Assert.Equal(.00019602 / .9802, chain[2].Smoothed[Ck], Tolerance);
    }

    [Theory]
    [InlineData("j", "p")]
    [InlineData("k", "q")]
    public void Case_02_the_same_port_of_another_parent_and_another_port_of_the_same_parent_evict_nothing(string parent, string port)
    {
        // o first on j (j .99, Out of sight .01), then on (k, p): j .99 · .01 = .0099, Out of sight .01 · .01 = 1e-4, k .01 · .99 + .99 · .99 = .99.
        // Both parents carry mass, so a key that forgot the parent or the port would evict o.
        SightingEvidence[] seen = [Seen("o", "main", 0, 1, "j"), Seen("o", "p", 30, 2, "k"), Seen("g", port, 60, 3, parent)];

        var chain = Run([], seen)["o"];

        Assert.Equal(2, chain.Count);
        Assert.Equal(seen[1].Provenance, chain[1].Provenance);
        Assert.Equal(.99, chain[1].Smoothed[Ck], Tolerance);
        Assert.Equal(.0099, chain[1].Smoothed[Cj], Tolerance);
        Assert.Equal(.0001, chain[1].Smoothed[Holder.OutOfSight], Tolerance);
    }

    [Theory]
    [InlineData("k", "q")]
    [InlineData("j", "p")]
    [InlineData(null, "main")]
    [InlineData(null, "backpack")]
    public void Case_03_a_part_seen_elsewhere_since_is_not_evicted(string? parent, string port)
    {
        // o on (k, p), then on another port of k, on another parent or on the body: its last port is no longer (k, p), and its mass
        // in k is .0099 or more, so only the last-port check keeps it.
        SightingEvidence[] seen = [Seen("o", "p", 0, 1, "k"), Seen("o", port, 30, 2, parent), Seen("g", "p", 60, 3, "k")];

        var chain = Run([], seen)["o"];

        Assert.Equal(2, chain.Count);
        Assert.Equal(seen[1].Provenance, chain[1].Provenance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Case_04a_two_geids_of_the_same_burst_evict_nothing(bool aBeforeB)
    {
        // Both lines of burst 1 are on (k, p), whichever comes first: the earlier one is not evicted by the later one.
        SightingEvidence first = Seen("a", "p", 0, 1, "k"), second = Seen("b", "p", 1, 1, "k");

        var chains = Run([], aBeforeB ? [first, second] : [second, first]);

        Assert.Single(chains["a"]);
        Assert.Single(chains["b"]);
        Assert.Equal(.99, chains["a"][0].Smoothed[Ck], Tolerance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Case_04b_a_complete_burst_leaves_the_eviction_to_the_listing_once(bool listingBeforeG)
    {
        // The listing has k and g, not o: o's mass in k, .99, goes once: .99 · .02 = .0198, Out of sight .01 + .99 · .98 = .9802.
        // An eviction by the port as well would leave .0198 · .02 = .000396.
        var old = Seen("o", "p", 0, 1, "k");
        var (listing, cur) = listingBeforeG ? (Seen("suit", "Body_ItemPort", 60, 2, cls: "suit"), Seen("g", "p", 61, 2, "k"))
            : (Seen("suit", "Body_ItemPort", 61, 2, cls: "suit"), Seen("g", "p", 60, 2, "k"));
        var boxK = Seen("k", "main", 62, 2, cls: "box");

        var chain = Run([], [old, listing, cur, boxK])["o"];

        Assert.Equal(2, chain.Count);
        Assert.Equal(listing.Provenance, chain[1].Provenance);
        Assert.Equal(.0198, chain[1].Smoothed[Ck], Tolerance);
        Assert.Equal(.9802, chain[1].Smoothed[Holder.OutOfSight], Tolerance);
    }

    [Fact]
    public void Case_05_the_name_of_a_body_port_on_a_parent_and_the_other_way_round_are_two_keys()
    {
        // o on k (k .99), then on the body's backpack (body .99, k .0099); g on backpack of k: o's last port is the body's, it is not the occupant of (k, backpack).
        var onK = Run([], [Seen("o", "main", 0, 1, "k"), Seen("o", "backpack", 30, 2), Seen("g", "backpack", 60, 3, "k")])["o"];

        Assert.Equal(2, onK.Count);

        // o on the body (body .99), then on backpack of k (body .0099): g on the body's backpack evicts nobody, o sits on (k, backpack).
        var onBody = Run([], [Seen("o", "main", 0, 1), Seen("o", "backpack", 30, 2, "k"), Seen("g", "backpack", 60, 3)])["o"];

        Assert.Equal(2, onBody.Count);
        Assert.Equal(.0099, onBody[1].Smoothed[Body], Tolerance);
    }

    [Fact]
    public void Case_06_a_chain_of_three_magazines_evicts_each_one_once()
    {
        // g replaces o: o k .0198. x replaces g, the last occupant: g k .99 · .02 = .0198. o is not the occupant any more: it keeps two entries.
        SightingEvidence[] seen = [Seen("o", "p", 0, 1, "k"), Seen("g", "p", 60, 2, "k"), Seen("x", "p", 120, 3, "k")];

        var chains = Run([], seen);

        Assert.Equal([2, 2, 1], [chains["o"].Count, chains["g"].Count, chains["x"].Count]);
        Assert.Equal(.0198, chains["o"][1].Smoothed[Ck], Tolerance);
        Assert.Equal(seen[2].Provenance, chains["g"][1].Provenance);
        Assert.Equal(.0198, chains["g"][1].Smoothed[Ck], Tolerance);
        Assert.Equal(.9802, chains["g"][1].Smoothed[Holder.OutOfSight], Tolerance);
    }

    [Fact]
    public void Case_07_a_lamp_replaced_on_the_scope_evicts_the_lamp_only()
    {
        // The scope is on the rifle (rifle .99); the old lamp on the scope (scope .99), replaced: scope .0198. The scope keeps one entry, rifle .99.
        SightingEvidence[] seen = [Seen("scope", "optic", 0, 1, "rifle"), Seen("lamp1", "rail", 1, 1, "scope"), Seen("lamp2", "rail", 60, 2, "scope")];

        var chains = Run([], seen);

        Assert.Equal(2, chains["lamp1"].Count);
        Assert.Equal(.0198, chains["lamp1"][1].Smoothed[new("Container", "scope")], Tolerance);
        Assert.Single(chains["scope"]);
        Assert.Equal(.99, chains["scope"][0].Smoothed[new("Container", "rifle")], Tolerance);
        Assert.Single(chains["lamp2"]);
    }

    [Fact]
    public void Case_08_a_port_of_the_body_outside_the_list_still_has_no_occupant()
    {
        // The rule of E7 is the one of DeathListingPortTests, which stays as it is; this one pins that a port with no parent needs to be on the list.
        Assert.Single(Run([], [Seen("o", "main", 0, 1), Seen("g", "main", 60, 2)])["o"]);
    }

    [Fact]
    public void Case_09a_a_part_not_evicted_since_it_was_seen_elsewhere_reads_the_line_of_the_new_one()
    {
        SightingEvidence[] seen = [Seen("o", "p", 0, 1, "k"), Seen("o", "q", 30, 2, "k"), Seen("g", "p", 60, 3, "k")];
        MoveEvidence[] moves = [Take("o", Ck, W, 90)];

        var chain = Run(moves, seen)["o"];

        Assert.Equal(3, chain.Count);
        Assert.Contains(seen[2].Provenance, chain[2].Evidence);
    }

    [Fact]
    public void Case_09b_the_list_of_a_part_the_listing_of_the_burst_spared_carries_the_listing_line()
    {
        // ε = 0. o leaves k at 70 s, after g's line (60 s) and before the listing (80 s), which then finds nothing of o in k. A list with g's
        // line and without the listing line would replay an eviction by the port at 60 s, when o still is in k: k 1 at 70 s becomes .02.
        SightingEvidence[] seen = [Seen("o", "p", 0, 1, "k"), Seen("g", "p", 60, 2, "k"), Seen("suit", "Body_ItemPort", 80, 2, cls: "suit"),
            Seen("k", "main", 81, 2, cls: "box")];
        MoveEvidence[] moves = [Take("o", Ck, W, 70), Take("o", W, U, 90)];

        Assert.Equal(1, Run(moves, seen, eps: 0)["o"][1].Predictive[Ck], Tolerance);
        Sufficient(moves, seen, eps: 0);
    }

    [Fact]
    public void Case_09c_the_list_of_a_part_carries_its_later_lines_of_the_burst()
    {
        // The move at 70 s has g's line in its list; without o's second line of burst 2 (80 s) the replay would see o in another burst
        // and evict it at 60 s: k .99 becomes .0198.
        SightingEvidence[] seen = [Seen("o", "p", 0, 1, "k"), Seen("g", "p", 60, 2, "k"), Seen("o", "q", 80, 2, "k")];
        MoveEvidence[] moves = [Take("o", Ck, W, 70)];

        Assert.Equal(.99, Run(moves, seen)["o"][1].Predictive[Ck], Tolerance);
        Sufficient(moves, seen);
    }

    [Fact]
    public void Case_09d_a_part_with_no_mass_in_k_is_not_evicted_but_its_list_reads_the_line_of_the_new_one()
    {
        // ε = 0: the move to W takes all of k's mass, exactly 0.
        SightingEvidence[] seen = [Seen("o", "p", 0, 1, "k"), Seen("g", "p", 60, 2, "k")];
        MoveEvidence[] moves = [Take("o", Ck, W, 30), Take("o", W, U, 90)];

        var chain = Run(moves, seen, eps: 0)["o"];

        Assert.Equal(3, chain.Count);
        Assert.Equal(1, chain[2].Predictive[W], Tolerance);
        Assert.Contains(seen[1].Provenance, chain[2].Evidence);
    }
}
