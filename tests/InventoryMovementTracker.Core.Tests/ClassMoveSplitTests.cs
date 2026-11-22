using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class ClassMoveSplitTests
{
    [Fact]
    public void Certain_candidates_fill_the_move_in_priority_order()
    {
        var (moved, shortfall) = ClassMoveSplit.Split([1, 1, 1], 2);

        Assert.Equal([1.0, 1.0, 0.0], moved);
        Assert.Equal([1.0, 0.0, 0.0], shortfall);
    }

    [Fact]
    public void Two_coin_flips_for_one_unit()
    {
        // Second moves only if the first is absent: 0.5 * 0.5. Nothing moves with probability 0.75.
        var (moved, shortfall) = ClassMoveSplit.Split([0.5, 0.5], 1);

        Assert.Equal([0.5, 0.25], moved);
        Assert.Equal([0.75, 0.25], shortfall);
    }

    [Fact]
    public void Matches_brute_force_over_every_presence_set()
    {
        double[] p = [0.9, 0.35, 0.6, 0.15, 0.7];
        const int n = 2;
        var expMoved = new double[p.Length];
        var expShort = new double[n + 1];
        for (var mask = 0; mask < 1 << p.Length; mask++)
        {
            var prob = 1.0;
            for (var i = 0; i < p.Length; i++)
            {
                var present = (mask >> i & 1) == 1;
                prob *= present ? p[i] : 1 - p[i];
            }
            var taken = 0;
            for (var i = 0; i < p.Length; i++)
                if ((mask >> i & 1) == 1 && taken++ < n) expMoved[i] += prob;
            expShort[n - Math.Min(taken, n)] += prob;
        }

        var (moved, shortfall) = ClassMoveSplit.Split(p, n);

        for (var i = 0; i < p.Length; i++) Assert.Equal(expMoved[i], moved[i], 12);
        for (var k = 0; k <= n; k++) Assert.Equal(expShort[k], shortfall[k], 12);
    }

    [Fact]
    public void No_candidate_leaves_the_whole_move_short()
    {
        var (moved, shortfall) = ClassMoveSplit.Split([], 3);

        Assert.Empty(moved);
        Assert.Equal([0.0, 0.0, 0.0, 1.0], shortfall);
    }

    [Fact]
    public void Units_are_conserved_in_expectation()
    {
        var (moved, shortfall) = ClassMoveSplit.Split([0.2, 0.8, 0.5, 0.4], 3);

        Assert.Equal(3, moved.Sum() + shortfall.Select((s, k) => s * k).Sum(), 12);
    }
}
