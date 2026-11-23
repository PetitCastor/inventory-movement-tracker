using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class EvidenceListTests
{
    private const double Eps = 0.01, Tolerance = 1e-12;
    private static readonly Holder Hand = new("Invalid", ""), S = new("Location", "S"), T = new("Container", "t"), U = new("Location", "U"), A = new("Container", "a"),
        B = new("Container", "b"), W = new("Location", "W");
    private long _line;

    private string Now() => new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero).AddMinutes(_line).ToString("O");

    private MoveEvidence Move(string type, string? geid, Holder source, Holder target, long units = 1, string? result = "succeed") =>
        new(new Provenance(1, ++_line, 0), Now(), type, "cls", geid, units, source.Kind, source.Key, target.Kind, target.Key, result, null, null);

    private MoveEvidence Store(string geid, Holder target) => Move("Store", geid, Hand, target);

    private MoveEvidence Take(string geid, Holder source, Holder target, string? result = "succeed") => Move("Interaction", geid, source, target, 1, result);

    private MoveEvidence Buy(long units = 1) => Move("Buy", null, new("Shop", ""), S, units);

    private MoveEvidence ClassMove(Holder source, Holder target, long units = 1, string? result = "succeed") => Move("Move", null, source, target, units, result);

    private MoveEvidence Close() => Move("Stack", null, S, S);

    private MoveEvidence MoveAll(Holder source, Holder target) => Move("MoveAll", null, source, target, 0);

    private SightingEvidence Seen(string geid, string port = "Backpack", string? parent = null, string cls = "cls") =>
        new(new Provenance(1, ++_line, 0), Now(), geid, cls, port, _line, parent, false);

    private static IReadOnlyDictionary<string, IReadOnlyList<ChainEntry>> Run(
        IEnumerable<MoveEvidence> moves, IEnumerable<SightingEvidence>? sightings = null, Hazards? hazards = null) =>
        NamedChains.Run(moves, sightings ?? [], Eps, delta: .02, hazards).Chains;

    [Fact]
    public void Case_01_a_purchase_then_each_named_proof()
    {
        MoveEvidence[] moves = [Buy(), Take("g", S, T), Take("g", T, U)];

        var chain = Run(moves)["g"];

        Assert.Equal([moves[0].Provenance], chain[0].Evidence);
        Assert.Equal([moves[0].Provenance, moves[1].Provenance], chain[1].Evidence);
    }

    [Fact]
    public void Case_01b_a_birth_from_a_lot_carries_the_births_after_it()
    {
        MoveEvidence[] moves = [Buy(2), Take("g1", S, T), Take("g2", S, T)];

        Assert.Equal(Of(moves, 0, 1), Run(moves)["g2"][0].Evidence);
    }

    [Fact]
    public void Case_02_a_class_move_carries_the_lot_it_fills()
    {
        MoveEvidence[] moves = [Buy(), ClassMove(S, T), Take("g", T, U)];

        Assert.Equal([moves[0].Provenance, moves[1].Provenance], Run(moves)["g"][0].Evidence);
    }

    [Fact]
    public void Case_03_a_geid_receives_only_from_the_places_before_its_own()
    {
        // g1 is born of a certain lot, so nothing of it is Out of sight, a place that would come after g2's in S.
        MoveEvidence[] moves = [Buy(), Store("g2", S), Take("g1", S, S), ClassMove(S, T), Take("g1", T, U), Take("g2", T, U)];

        var chains = Run(moves);

        Assert.Equal(Of(moves, 0, 2, 3), chains["g1"][^1].Evidence);
        Assert.Equal(Of(moves, 0, 1, 2, 3), chains["g2"][^1].Evidence);
    }

    [Fact]
    public void Case_04_a_complete_listing_without_the_geid()
    {
        var seen = Seen("g");
        var body = Seen("suit", "Body_ItemPort");

        var chain = Run([Take("g", U, T)], [seen, body])["g"];

        Assert.Equal([seen.Provenance, body.Provenance], chain[^1].Evidence);
    }

    [Fact]
    public void Case_05_a_reset_cuts_the_lists()
    {
        MoveEvidence[] before = [Buy(), Take("g", S, T)];
        var reset = new SessionEvidence(2, before[1].Ts, null, 1, "spawn", null, DateTimeOffset.Parse(before[1].Ts).AddSeconds(1).ToString("O"));
        MoveEvidence[] after = [Take("g", T, U), Take("g", U, S)];

        var chain = ResetCut.Run([.. before, .. after], [], [reset], Eps, Eps).Chains["g"];

        Assert.Equal([before[0].Provenance], chain[0].Evidence);
        Assert.Empty(chain[1].Evidence);
        Assert.Equal([after[0].Provenance], chain[2].Evidence);
    }

    [Fact]
    public void A_list_is_in_order_of_session_then_offset_then_index()
    {
        // Session 2 has the lower offsets; the lines are given last first, so only the sort can put them in order.
        MoveEvidence From(long session, long offset, long index, MoveEvidence m) => m with { Provenance = new(session, offset, index) };
        MoveEvidence[] moves = [From(1, 9, 0, Store("g", S)), From(2, 1, 0, Take("g", S, T)), From(2, 1, 1, Take("g", T, U)), From(2, 3, 0, Take("g", U, W)),
            From(2, 4, 0, Take("g", W, S))];

        Assert.Equal(Of(moves, 0, 1, 2, 3), Run(Enumerable.Reverse(moves))["g"][^1].Evidence);
    }

    [Fact]
    public void Case_06_a_lost_queue_carries_the_lot_it_proves()
    {
        MoveEvidence[] moves = [ClassMove(S, T, result: "lost"), ClassMove(S, T, result: "lost"), Close(), Take("g", S, U)];

        Assert.Equal(Of(moves, 0, 1, 2), Run(moves)["g"][0].Evidence);
    }

    [Fact]
    public void Case_06b_a_lost_geid_whose_source_is_not_weighed_receives_its_queue()
    {
        // Its own line, and the closing one, which comes after it, are lines of the queue: only the former is left out.
        MoveEvidence[] moves = [Store("g", S), Take("g", S, T, "lost"), Take("g", T, U, "lost"), Close()];

        var chain = Run(moves)["g"];

        Assert.Null(chain[2].SourceProbability);
        Assert.Equal(Of(moves, 0, 1, 3), chain[2].Evidence);
    }

    [Fact]
    public void Case_06c_the_geids_at_the_source_carry_what_the_queue_proves()
    {
        MoveEvidence[] moves = [Store("h", S), ClassMove(S, T, 2, "lost"), Close(), Take("g", S, U)];

        Assert.Equal(Of(moves, 0, 1, 2), Run(moves)["g"][0].Evidence);
    }

    [Fact]
    public void A1_a_lost_geid_receives_its_queue_even_when_its_source_is_weighed()
    {
        // Without the closing line, the two queues would merge and the second lost move would not weigh T.
        MoveEvidence[] moves = [Store("g", T), Take("g", S, T, "lost"), Close(), Take("g", T, U, "lost")];

        Assert.Equal(Of(moves, 0, 1, 2), Run(moves)["g"][2].Evidence);
        Assert.Equal(2, Checks(moves, []));
    }

    [Fact]
    public void A2_the_geids_a_queue_names_bring_their_own_evidence()
    {
        // Without its first line, h would be born from the lot that the queue proves in S.
        MoveEvidence[] moves = [Store("h", U), Take("h", S, T, "lost"), Close(), Take("o", S, T), Take("o", S, W)];

        Assert.Equal(Of(moves, 0, 1, 2, 3), Run(moves)["o"][1].Evidence);
        Assert.Equal(2, Checks(moves, []));
    }

    [Fact]
    public void A2_a_lost_geid_receives_what_the_geids_of_its_queue_had()
    {
        MoveEvidence[] moves = [Store("h", U), Take("h", S, T, "lost"), Take("g", T, W, "lost"), Close()];

        Assert.Equal(Of(moves, 0, 1, 3), Run(moves)["g"][0].Evidence);
    }

    [Fact]
    public void A3_a_first_proof_reads_an_empty_lot_too()
    {
        // g3 is born from the lot of T before g1, which finds it empty.
        MoveEvidence[] moves = [Take("g3", T, S, "lost"), ClassMove(S, B), Take("g1", T, S, "lost"), Take("g1", T, T)];

        Assert.Equal(Of(moves, 0, 1, 2), Run(moves)["g1"][1].Evidence);
        Assert.Equal(1, Checks(moves, []));
    }

    [Theory]
    [InlineData("F1 class move")]
    [InlineData("F1 queue")]
    [InlineData("F2")]
    [InlineData("F3")]
    [InlineData("F4")]
    [InlineData("F5")]
    [InlineData("F7")]
    [InlineData("F8")]
    [InlineData("F9")]
    public void A_list_is_sufficient_when_what_is_absent_matters(string gap)
    {
        Holder corpse = new("Corpse", "k"), body = Holder.Body;
        MoveEvidence Box(MoveEvidence m) => m with { ItemClass = "box" };
        MoveEvidence LaterSession(MoveEvidence m) => m with { Provenance = m.Provenance with { SessionId = 2 } };
        object[] stream = gap switch
        {
            // a, known from the lot of S, has left U whole: replayed there, it would arrive in T with o, and rank before it.
            "F1 class move" => [Buy(2), Take("a", S, U), ClassMove(U, T), Take("o", S, U), ClassMove(U, T, 2), ClassMove(T, W), Take("o", W, S)],
            // a, known from p, has left U whole before the queue: replayed there, it would cover what the queue proves.
            "F1 queue" => [Buy(2), Take("a", S, U), ClassMove(U, T), ClassMove(U, A, 1, "lost"), Close(), Take("o", U, T), Take("p", S, T), ClassMove(T, W), Take("o", W, S)],
            // c is seen after the queue's first line, before its own.
            "F2" => [Take("g3", body, corpse, "lost"), Seen("c", parent: "a", cls: "box"), Box(Take("c", A, U, "lost")), Box(Take("b", A, T))],
            // g2, in c, which left the body, stays out of the last move from the body.
            "F3" => [Seen("c", cls: "box"), Take("g2", A, B, "lost"), Seen("g2", parent: "c"), ClassMove(body, A), Take("g3", body, S, "lost"), Take("g3", body, body),
                Box(Take("c", U, W)), Move("Store", "g2", Hand, A, result: "lost"), ClassMove(body, A, 3), Seen("g2")],
            // The second move takes nothing from the lot of T, which g4 spares.
            "F4" => [ClassMove(B, T, 2, null), Seen("g4", "Body_ItemPort"), ClassMove(body, T, 3, "lost"), ClassMove(T, B, 2), Move("Drop", "g3", body, new("World", "w"))],
            // t holds the target, so it stays out of the MoveAll, which its list has as the line that closes its queue.
            "F7" => [Store("t", U), Store("b", T), Take("t", U, B, "lost"), MoveAll(U, B), Take("t", U, S)],
            // The class move, which closes a queue, gives nothing to the lot of the body: replayed without g2, it would, and g1 would be born there.
            "F8" => [ClassMove(T, body, 2, "lost"), Take("g2", T, T, null), ClassMove(T, body), Store("g1", S), Take("g1", S, U, "lost")],
            // b has left the body when the class move that closes the queue spares its lot: replayed on the body, it would serve it.
            "F9" => [Seen("b", cls: "box"), ClassMove(W, B), ClassMove(U, S), Box(Take("b", body, U)), ClassMove(B, U, 1, "lost"), ClassMove(U, S), Take("o", B, W)],
            // The queue of g4 would merge with the one before, whose opening line closes the first.
            _ => [Box(Take("a", U, S, "lost")), LaterSession(ClassMove(T, body, 3, "lost")), LaterSession(MoveAll(S, W)),
                LaterSession(Take("g4", body, corpse, "lost")), LaterSession(Box(Take("a", U, S)))],
        };

        Checks([.. stream.OfType<MoveEvidence>()], [.. stream.OfType<SightingEvidence>()]);
    }

    [Fact]
    public void A_class_move_carries_the_containers_it_reads_for_its_carriers()
    {
        SightingEvidence[] seen = [Seen("p", cls: "bag"), Seen("b", parent: "p", cls: "bag"), Seen("g", parent: "b")];
        MoveEvidence[] moves = [ClassMove(S, T), Take("g", T, U)];

        Assert.Equal([.. seen.Select(x => x.Provenance), moves[0].Provenance], Run(moves, seen)["g"][^1].Evidence);
    }

    [Fact]
    public void Case_07b_a_move_all_carries_the_lot_it_brings()
    {
        MoveEvidence[] moves = [Buy(), MoveAll(S, T), Take("g", T, U)];

        Assert.Equal(Of(moves, 0, 1), Run(moves)["g"][0].Evidence);
    }

    [Fact]
    public void Case_07_a_move_all_carries_the_containers_of_its_target()
    {
        MoveEvidence[] moves = [Store("t", A), Store("a", W), Store("g", S), MoveAll(S, T), Take("g", T, U)];

        Assert.Equal(Of(moves, 0, 1, 2, 3), Run(moves)["g"][^1].Evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Case_08_replaying_a_list_gives_back_the_source_probability(bool withHazards)
    {
        var hazards = withHazards ? Hazards.Default : null;
        var (moves, sightings) = Mixed();

        Assert.Equal(11, Checks(moves, sightings, hazards));
    }

    // Replays each entry's list with the entry's line, compares the whole law; returns the number of source probabilities compared.
    private static int Checks(MoveEvidence[] moves, SightingEvidence[] sightings, Hazards? hazards = null)
    {
        var checks = 0;
        foreach (var (geid, chain) in Run(moves, sightings, hazards))
            foreach (var entry in chain)
            {
                var kept = entry.Evidence.Append(entry.Provenance).ToHashSet();
                var replayed = Run(moves.Where(m => kept.Contains(m.Provenance)), sightings.Where(s => kept.Contains(s.Provenance)), hazards)[geid]
                    .Single(e => e.Provenance == entry.Provenance);

                foreach (var holder in entry.Predictive.Keys.Union(replayed.Predictive.Keys))
                    Assert.Equal(entry.Predictive.GetValueOrDefault(holder), replayed.Predictive.GetValueOrDefault(holder), Tolerance);
                Assert.Equal(entry.SourceProbability.HasValue, replayed.SourceProbability.HasValue);
                if (entry.SourceProbability is not { } p) continue;
                Assert.Equal(p, replayed.SourceProbability!.Value, Tolerance);
                checks++;
            }
        return checks;
    }

    // Cases 1 to 7 in one stream, with a container carried on the body.
    private (MoveEvidence[] Moves, SightingEvidence[] Sightings) Mixed()
    {
        var sightings = new List<SightingEvidence>();
        void See(string geid, string port = "Backpack") => sightings.Add(Seen(geid, port));
        var moves = new List<MoveEvidence> { Buy(3), Take("g1", S, T), Store("g2", S), Store("g3", S), ClassMove(S, T), Take("g2", T, S), Take("g3", T, U) };
        See("g4");
        See("b");
        moves.AddRange([Take("g5", S, B), ClassMove(W, U)]);
        See("suit", "Body_ItemPort");
        moves.AddRange([Take("g4", U, S), ClassMove(S, T, result: "lost"), Take("g4", S, T, "lost"), Take("g4", T, U, "lost"), Close(), Take("g6", S, T)]);
        moves.AddRange([Store("t", A), Store("a", W), MoveAll(S, T), Take("g2", T, U), Take("g5", B, S), ClassMove(T, S, 2)]);
        moves.AddRange([Take("g1", S, U), Take("g4", T, U), Take("g6", S, U), Take("g2", U, T), Take("g3", U, T)]);
        return ([.. moves], [.. sightings]);
    }

    private static IEnumerable<Provenance> Of(MoveEvidence[] moves, params int[] indices) => indices.Select(i => moves[i].Provenance);
}
