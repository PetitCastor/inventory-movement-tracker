using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class DeathListingPortTests
{
    private const double Eps = .01, Delta = .02, Tolerance = 1e-9;
    private static readonly Holder Body = Holder.Body, W = new("Location", "W"), U = new("Location", "U"), K = new("Corpse", "k"), Bag = new("Container", "bag");
    private long _line;

    private static string At(double seconds) => new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero).AddSeconds(seconds).ToString("O");

    private MoveEvidence Move(string type, string? geid, Holder source, Holder target, double seconds, long units = 1, string cls = "cls",
        string result = "succeed", long session = 1) =>
        new(new Provenance(session, ++_line, 0), At(seconds), type, cls, geid, units, source.Kind, source.Key, target.Kind, target.Key, result, null, null);

    private MoveEvidence Buy(long units, double seconds, Holder? target = null) => Move("Buy", null, new("Shop", ""), target ?? Body, seconds, units);

    private MoveEvidence Take(string geid, Holder source, Holder target, double seconds) => Move("Interaction", geid, source, target, seconds);

    private MoveEvidence Death(string geid, double seconds, string cls = "cls", string result = "succeed", long session = 1, Holder? corpse = null) =>
        Move("Death", geid, Body, corpse ?? K, seconds, 1, cls, result, session);

    private SightingEvidence Seen(string geid, string port, double seconds, long burst, string? parent = null, string cls = "cls") =>
        new(new Provenance(1, ++_line, 0), At(seconds), geid, cls, port, burst, parent, false);

    private static (IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Chains, IReadOnlyDictionary<(Holder, string), double> Lots) Run(
        IEnumerable<MoveEvidence> moves, IEnumerable<SightingEvidence>? seen = null, double eps = Eps)
    {
        var run = NamedChains.Run(moves, seen ?? [], eps, Delta);
        return (run.Chains, run.Lots);
    }

    // Replays each entry's list with the entry's line and compares the whole predictive law.
    private static void Sufficient(MoveEvidence[] moves, SightingEvidence[] seen, double eps = Eps)
    {
        foreach (var (geid, chain) in Run(moves, seen, eps).Chains)
            foreach (var entry in chain)
            {
                var kept = entry.Evidence.Append(entry.Provenance).ToHashSet();
                var replayed = Run(moves.Where(m => kept.Contains(m.Provenance)), seen.Where(s => kept.Contains(s.Provenance)), eps).Chains[geid]
                    .Single(e => e.Provenance == entry.Provenance);

                foreach (var holder in entry.Predictive.Keys.Union(replayed.Predictive.Keys))
                    Assert.Equal(entry.Predictive.GetValueOrDefault(holder), replayed.Predictive.GetValueOrDefault(holder), 1e-12);
            }
    }

    [Theory]
    [InlineData("Armor_Torso")]
    [InlineData("wep_stocked_primary")]
    [InlineData("backpack")]
    [InlineData("wep_sidearm")]
    public void Case_01_a_body_port_taken_by_another_geid_in_another_burst_evicts_the_old_one(string port)
    {
        // old, seen once, is at the body with 1 − ε = .99 and Out of sight with .01. The eviction sends 1 − δ = .98 of the body:
        // body .99 · .02 = .0198, Out of sight .01 + .99 · .98 = .9802.
        SightingEvidence old = Seen("old", port, 0, 1), cur = Seen("new", port, 60, 2);

        var chains = Run([], [old, cur]).Chains;

        Assert.Equal(2, chains["old"].Count);
        Assert.Equal(cur.Provenance, chains["old"][1].Provenance);
        Assert.Equal(.99, chains["old"][1].Predictive[Body], Tolerance);
        Assert.Equal(.0198, chains["old"][1].Smoothed[Body], Tolerance);
        Assert.Equal(.9802, chains["old"][1].Smoothed[Holder.OutOfSight], Tolerance);
        Assert.Single(chains["new"]);
    }

    [Fact]
    public void Case_01b_the_occupant_is_the_last_geid_seen_on_the_port()
    {
        // c evicts b, not a again: a keeps its two entries (its sight, its eviction), b has two, c one.
        var chains = Run([], [Seen("a", "Armor_Torso", 0, 1), Seen("b", "Armor_Torso", 60, 2), Seen("c", "Armor_Torso", 120, 3)]).Chains;

        Assert.Equal([2, 2, 1], [chains["a"].Count, chains["b"].Count, chains["c"].Count]);
        Assert.Equal(.0198, chains["b"][1].Smoothed[Body], Tolerance);
    }

    [Fact]
    public void Case_01d_a_sight_in_a_container_takes_no_port()
    {
        // The piece c, seen with a parent on a port named backpack, does not become its occupant: g evicts a.
        SightingEvidence[] seen = [Seen("a", "backpack", 0, 1), Seen("c", "backpack", 30, 2, parent: "bag"), Seen("g", "backpack", 60, 3)];
        var chains = Run([], seen).Chains;

        Assert.Equal(2, chains["a"].Count);
        Assert.Equal(seen[2].Provenance, chains["a"][1].Provenance);
        Assert.Single(chains["c"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Case_01e_a_complete_listing_in_the_burst_has_evicted_the_old_occupant_once(bool listingFirst)
    {
        // The listing alone sends .98 of the body: .99 · .02 = .0198. A second eviction by the port would leave .0198 · .02 = .000396.
        var listing = () => Seen("suit", "Body_ItemPort", listingFirst ? 60 : 61, 2, cls: "suit");
        var old = Seen("old", "Armor_Torso", 0, 1);
        var (first, second) = listingFirst ? (listing(), Seen("new", "Armor_Torso", 61, 2)) : (Seen("new", "Armor_Torso", 60, 2), listing());

        var chain = Run([], [old, first, second]).Chains["old"];

        Assert.Equal(2, chain.Count);
        Assert.Equal(.0198, chain[1].Smoothed[Body], Tolerance);
    }

    [Fact]
    public void Case_01f_the_list_of_a_geid_a_listing_did_not_evict_carries_the_listing_line()
    {
        // ε = 0. old leaves the body at 70 s, after new's line (60 s) and before the listing (80 s) that finds it empty. A list with
        // new's line and without the listing line would replay an eviction by the port at 60 s, when old still is at the body.
        SightingEvidence[] seen = [Seen("old", "Armor_Torso", 0, 1), Seen("new", "Armor_Torso", 60, 2), Seen("suit", "Body_ItemPort", 80, 2, cls: "suit")];
        MoveEvidence[] moves = [Take("old", Body, W, 70), Take("old", W, U, 90)];

        var chain = Run(moves, seen, eps: 0).Chains["old"];

        Assert.Equal(3, chain.Count);
        Assert.Equal(1, chain[1].Predictive[Body], Tolerance);
        Sufficient(moves, seen, eps: 0);
    }

    [Fact]
    public void Case_01c_an_eviction_is_carried_by_the_list_of_what_follows()
    {
        SightingEvidence[] seen = [Seen("old", "Armor_Torso", 0, 1), Seen("new", "Armor_Torso", 60, 2), Seen("old", "main", 120, 3)];

        Sufficient([], seen);
    }

    [Theory]
    [InlineData("backpack")]
    [InlineData("main")]
    public void Case_02_a_geid_seen_on_another_port_is_not_evicted(string other)
    {
        // old: body .99, Out of sight .01; a second sight keeps the body row and sends .99 of Out of sight there: .99 + .01 · .99 = .9999.
        SightingEvidence[] seen = [Seen("old", "Armor_Torso", 0, 1), Seen("old", other, 30, 2), Seen("new", "Armor_Torso", 60, 3)];
        MoveEvidence[] moves = [Take("old", Body, W, 90)];

        var chain = Run(moves, seen).Chains["old"];

        Assert.Equal(3, chain.Count);
        Assert.Equal(.9999, chain[2].Predictive[Body], Tolerance);
        Assert.Contains(seen[2].Provenance, chain[2].Evidence);
    }

    [Fact]
    public void Case_02b_a_geid_seen_in_a_container_is_not_evicted_and_keeps_its_crumb_at_the_body()
    {
        // The sight in the bag keeps ε of each row at its place: body .99 · .01 = .0099, Out of sight .01 · .01 = 1e-4, bag .99.
        // Sent away by an eviction, the body would hold .0099 · .02.
        SightingEvidence[] seen = [Seen("old", "Armor_Torso", 0, 1), Seen("old", "backpack", 30, 2, parent: "bag"), Seen("new", "Armor_Torso", 60, 3)];

        var chain = Run([], seen).Chains["old"];

        Assert.Equal(2, chain.Count);
        Assert.Equal(.0099, chain[1].Smoothed[Body], Tolerance);
        Assert.Equal(.99, chain[1].Smoothed[new("Container", "bag")], Tolerance);
    }

    [Fact]
    public void Case_02c_a_geid_with_no_mass_at_the_body_is_not_evicted_but_its_list_reads_the_sight()
    {
        // ε = 0: the move to W takes all of the body's mass, exactly 0.
        SightingEvidence[] seen = [Seen("old", "Armor_Torso", 0, 1), Seen("new", "Armor_Torso", 60, 2)];
        MoveEvidence[] moves = [Take("old", Body, W, 30), Take("old", W, U, 90)];

        var chain = Run(moves, seen, eps: 0).Chains["old"];

        Assert.Equal(3, chain.Count);
        Assert.Equal(1, chain[2].Predictive[W], Tolerance);
        Assert.Contains(seen[1].Provenance, chain[2].Evidence);
    }

    [Fact]
    public void Case_03_a_geid_of_the_same_burst_is_not_evicted_whichever_line_comes_first()
    {
        SightingEvidence earlier = Seen("old", "Armor_Torso", 0, 1), cur = Seen("new", "Armor_Torso", 1, 1);
        var first = Run([Take("old", Body, W, 60)], [earlier, cur]).Chains["old"];

        Assert.Equal(2, first.Count);
        Assert.Equal(.99, first[1].Predictive[Body], Tolerance);
        Assert.Contains(cur.Provenance, first[1].Evidence);

        // Here old's line of burst 2 comes after new's, which is the sight that decides.
        SightingEvidence[] seen = [Seen("old", "Armor_Torso", 0, 1), Seen("new", "Armor_Torso", 60, 2), Seen("old", "main", 61, 2)];

        Assert.Equal(2, Run([], seen).Chains["old"].Count);
    }

    [Fact]
    public void Case_03b_the_list_of_the_old_occupant_carries_its_later_lines_of_the_burst()
    {
        // The move between new's line and old's second line of burst 2 has new's line in its list; without old's later line the
        // replay would see old in another burst and evict it.
        SightingEvidence[] seen = [Seen("old", "Armor_Torso", 0, 1), Seen("new", "Armor_Torso", 60, 2)];
        MoveEvidence[] moves = [Take("old", Body, W, 70)];
        SightingEvidence[] all = [.. seen, Seen("old", "main", 80, 2)];

        Sufficient(moves, all);
        Assert.Contains(all[2].Provenance, Run(moves, all).Chains["old"][1].Evidence);
    }

    [Fact]
    public void Case_03c_a_geid_seen_again_on_its_port_does_not_evict_itself()
    {
        Assert.Equal(2, Run([], [Seen("old", "Armor_Torso", 0, 1), Seen("old", "Armor_Torso", 60, 2)]).Chains["old"].Count);
    }

    [Fact]
    public void Case_02d_a_sight_in_a_container_on_a_port_of_the_same_name_is_not_a_sight_on_that_port()
    {
        // old, seen on Armor_Torso, is seen in a bag (parent) under the same port name: its last port is no longer Armor_Torso.
        SightingEvidence[] seen = [Seen("old", "Armor_Torso", 0, 1), Seen("old", "Armor_Torso", 30, 2, parent: "bag"), Seen("new", "Armor_Torso", 60, 3)];

        var chain = Run([], seen).Chains["old"];

        Assert.Equal(2, chain.Count);
        Assert.Equal(.0099, chain[1].Smoothed[Body], Tolerance);
    }

    [Theory]
    [InlineData("magazine_attach_1")]
    [InlineData("main")]
    [InlineData("backpack_1")]
    [InlineData("wep_sidearm_1")]
    [InlineData("Armor")]
    [InlineData("wep_stocked")]
    public void Case_04_a_port_outside_the_list_has_no_occupant(string port)
    {
        Assert.Single(Run([], [Seen("old", port, 0, 1), Seen("new", port, 60, 2)]).Chains["old"]);
    }

    [Fact]
    public void Case_05_the_lot_goes_to_the_corpse_at_the_last_line_of_the_death()
    {
        // Three units. g1, new at the first line, takes one (2 left), g2 at the second takes one (1 left); the transfer follows.
        MoveEvidence[] moves = [Buy(3, -1), Death("g1", 0), Death("g2", 2)];

        var run = Run(moves);

        Assert.Equal(1, run.Lots[(K, "cls")], Tolerance);
        Assert.Equal(0, run.Lots[(Body, "cls")], Tolerance);
        Assert.Equal(1, run.Chains["g1"][0].Predictive[Body], Tolerance);
        // A death under 5 s long is read through its last line alone, read before the first step.
        Assert.Contains(moves[2].Provenance, run.Chains["g1"][0].Evidence);
    }

    [Fact]
    public void Case_05c_a_short_death_carries_its_last_line_only_and_a_long_one_all_its_lines()
    {
        // 0, 2, 4 s: 4 s long, the middle line is not carried. 0, 4, 8 s: 8 s long, it is.
        MoveEvidence[] shortDeath = [Buy(5, -1), Death("g1", 0), Death("g2", 2), Death("g3", 4)];
        MoveEvidence[] longDeath = [Buy(5, -1), Death("g1", 0), Death("g2", 4), Death("g3", 8)];

        Assert.DoesNotContain(shortDeath[2].Provenance, Run(shortDeath).Chains["g1"][0].Evidence);
        Assert.Contains(shortDeath[3].Provenance, Run(shortDeath).Chains["g1"][0].Evidence);
        Assert.Contains(longDeath[2].Provenance, Run(longDeath).Chains["g1"][0].Evidence);
    }

    [Fact]
    public void Case_09g_the_list_of_a_reader_inside_a_long_death_carries_all_its_lines()
    {
        // h reads at 5 s a lot that the death, ending at 8 s, has not yet moved. A list with the last line alone and not the middle one
        // would replay two deaths, 0 and 8 s, and the first would empty the lot before h.
        MoveEvidence[] moves = [Buy(4, -1), Death("g1", 0), Death("g2", 4), Take("h", Body, W, 5), Death("g3", 8)];

        Sufficient(moves, []);
    }

    [Fact]
    public void Case_05b_a_death_of_one_line_transfers_after_that_line_has_taken_its_unit()
    {
        // Two units, g1 takes one: the corpse receives the other. Before g1's step it would receive both.
        var run = Run([Buy(2, -1), Death("g1", 0)]);

        Assert.Equal(1, run.Lots[(K, "cls")], Tolerance);
        Assert.Equal(0, run.Lots[(Body, "cls")], Tolerance);
    }

    [Theory]
    [InlineData(6, 1, "succeed")]
    [InlineData(5, 1, "succeed")]
    [InlineData(3, 2, "succeed")]
    [InlineData(6, 1, "failed")]
    [InlineData(6, 1, "lost")]
    public void Case_06_two_deaths_of_the_same_corpse_make_two_transfers(double secondAt, long session, string ignored)
    {
        // Two units, g1 takes one, 1 goes to the corpse. h, at 1 s, finds the body empty: not born (Out of sight 1). Two more units,
        // g2 takes one, 1 goes: 2 at the corpse. Run as one death, h would be born of the lot and the corpse would hold 1.
        // A Death line of another class, failed or lost, at 3 s does not join the two.
        List<MoveEvidence> moves = [Buy(2, -1), Death("g1", 0), Take("h", Body, W, 1), Buy(2, 2)];
        if (ignored != "succeed") moves.Add(Death("x", 3, "other", ignored));
        moves.Add(Death("g2", secondAt, "cls", "succeed", session));

        var run = Run(moves);

        Assert.Equal(1, run.Chains["h"][0].Predictive[Holder.OutOfSight], Tolerance);
        Assert.Equal(2, run.Lots[(K, "cls")], Tolerance);
        Assert.Equal(0, run.Lots[(Body, "cls")], Tolerance);
    }

    [Fact]
    public void Case_06c_two_corpses_make_two_deaths_even_within_5_s()
    {
        Holder other = new("Corpse", "k2");

        var run = Run([Buy(2, -1), Death("g1", 0), Take("h", Body, W, 1), Buy(2, 2), Death("g2", 3, corpse: other)]);

        Assert.Equal(1, run.Chains["h"][0].Predictive[Holder.OutOfSight], Tolerance);
        Assert.Equal(1, run.Lots[(K, "cls")], Tolerance);
        Assert.Equal(1, run.Lots[(other, "cls")], Tolerance);
        Assert.Equal(0, run.Lots[(Body, "cls")], Tolerance);
    }

    [Fact]
    public void Case_06d_two_lines_4_s_apart_are_one_death()
    {
        // h, at 1 s, finds the lot (1 unit) and takes it; g2 at 4 s takes one of the two bought: 1 goes to the corpse, once.
        var run = Run([Buy(2, -1), Death("g1", 0), Take("h", Body, W, 1), Buy(2, 2), Death("g2", 4)]);

        Assert.Equal(1, run.Chains["h"][0].Predictive[Body], Tolerance);
        Assert.Equal(1, run.Lots[(K, "cls")], Tolerance);
    }

    [Fact]
    public void Case_06e_a_death_line_without_a_corpse_is_no_death()
    {
        var run = Run([Buy(1, -1), Move("Death", "g1", Body, new("Invalid", ""), 0)]);

        Assert.DoesNotContain(run.Lots.Keys, k => k.Item1.Kind == "Corpse");
    }

    [Fact]
    public void Case_06b_a_lot_in_a_container_stays_there_and_a_body_with_no_lot_makes_none_at_the_corpse()
    {
        var run = Run([Buy(1, -1, Bag), Death("g1", 0)]);

        Assert.Equal(1, run.Lots[(Bag, "cls")], Tolerance);
        Assert.False(run.Lots.ContainsKey((K, "cls")));
    }

    [Fact]
    public void Case_07_a_listing_without_the_class_thins_the_lot_and_with_it_leaves_it()
    {
        // Two units, each kept with δ = .02: the mean is 2 · .02 = .04.
        MoveEvidence[] moves = [Buy(2, -1)];
        var suit = Seen("suit", "Body_ItemPort", 0, 5, cls: "suit");

        Assert.Equal(.04, Run(moves, [suit]).Lots[(Body, "cls")], Tolerance);
        // A piece of the class counts: nothing happens.
        Assert.Equal(2, Run(moves, [suit, Seen("m1", "magazine_attach_1", 1, 5, parent: "w")]).Lots[(Body, "cls")], Tolerance);
        // A geid of the class takes its unit on arrival (R50) and the listing does not thin what is left.
        Assert.Equal(1, Run(moves, [Seen("g", "backpack", -1, 5), suit]).Lots[(Body, "cls")], Tolerance);
    }

    [Fact]
    public void Case_08_a_burst_without_the_body_line_changes_nothing()
    {
        Assert.Equal(2, Run([Buy(2, -1)], [Seen("x", "backpack", 0, 5, cls: "other")]).Lots[(Body, "cls")], Tolerance);
    }

    [Fact]
    public void Case_09_the_list_of_a_geid_that_reads_the_lot_carries_the_death()
    {
        // At 1 s h reads a lot that the death has not yet moved: it needs all the lines of the death, the last included.
        MoveEvidence[] moves = [Buy(4, -1), Death("g1", 0), Take("h", Body, W, 1), Death("g2", 2)];

        Sufficient(moves, []);
    }

    [Fact]
    public void Case_09b_a_lot_born_inside_the_death_receives_its_lines_at_the_transfer()
    {
        // The lot is born after the first line, so the first line did not give it the lines; g3 is known, so no birth marks it.
        // h2 reads it after the transfer: empty, it is not born (Out of sight 1).
        MoveEvidence[] moves = [Take("g3", W, Body, -5), Death("g1", 0), Buy(2, 1), Death("g3", 2), Take("h2", Body, U, 10)];

        Assert.Equal(1, Run(moves).Chains["h2"][0].Predictive[Holder.OutOfSight], Tolerance);
        Sufficient(moves, []);
    }

    [Fact]
    public void Case_09f_a_lot_born_inside_the_death_reads_all_its_lines_before_the_next_step()
    {
        // The lot is born at 1 s, after the first line. g2 takes one of its two units at 2 s, h the other at 3 s, before the death ends
        // at 4 s: h's list needs the line at 4 s, or its replay would end the death at 2 s and find the lot empty.
        MoveEvidence[] moves = [Death("g1", 0), Buy(2, 1), Death("g2", 2), Take("h", Body, W, 3), Death("g4", 4)];

        Assert.Equal(1, Run(moves).Chains["h"][0].Predictive[Body], Tolerance);
        Sufficient(moves, []);
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(5.5)]
    public void Case_09h_a_death_of_5_s_or_more_carries_all_its_lines(double lastAt)
    {
        // The gaps (2.5 s, then 2.5 or 3 s) chain the three lines into one death, whose span is not under 5 s. g2 is known, so no
        // birth marks its line: h reads it only through the death's evidence, without which the replay splits the death in two.
        MoveEvidence[] moves = [Take("g2", W, Body, -5), Buy(4, -1), Death("g1", 0), Death("g2", 2.5), Take("h", Body, W, 3), Death("g3", lastAt)];

        Sufficient(moves, []);
    }

    [Fact]
    public void Case_09c_the_list_of_a_geid_that_reads_the_lot_carries_the_listing()
    {
        // The listing leaves 2 · .02 = .04 units, P(at least one) = 1 − .98² = .0396: h is born of it with that.
        MoveEvidence[] moves = [Buy(2, -1), Take("h", Body, W, 10)];
        SightingEvidence[] seen = [Seen("suit", "Body_ItemPort", 0, 5, cls: "suit")];

        Assert.Equal(.0396, Run(moves, seen).Chains["h"][0].Predictive[Body], Tolerance);
        Sufficient(moves, seen);
    }

    [Fact]
    public void Case_09d_the_list_of_a_geid_that_reads_the_lot_carries_the_line_that_has_its_class()
    {
        // A list with the Body_ItemPort line and not the piece would see the class absent, and thin the lot.
        MoveEvidence[] moves = [Buy(2, -1), Take("h", Body, W, 10)];
        // The list holds the first line of the class, not the second.
        SightingEvidence[] seen = [Seen("suit", "Body_ItemPort", 0, 5, cls: "suit"), Seen("m1", "magazine_attach_1", 1, 5, parent: "w"),
            Seen("m2", "magazine_attach_1", 2, 5, parent: "w")];

        var chain = Run(moves, seen).Chains["h"][0];

        Assert.Equal(1, chain.Predictive[Body], Tolerance);
        Assert.Contains(seen[1].Provenance, chain.Evidence);
        Assert.DoesNotContain(seen[2].Provenance, chain.Evidence);
        Sufficient(moves, seen);
    }
}
