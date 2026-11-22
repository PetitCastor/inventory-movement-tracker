using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class ObjectChainTests
{
    private const double Eps = 0.01;

    // P0.1 §4: M = diag(source likelihood) · J_h, with J_h[y, j] = ε·[y = j] + (1 − ε)·[j = h].
    // The source likelihood is 1 − ε if X = source, else ε; no source means a plain jump.
    private static double[,] Move(int k, int? source, int h)
    {
        var m = new double[k, k];
        for (var y = 0; y < k; y++)
            for (var j = 0; j < k; j++)
                m[y, j] = (source is null ? 1 : y == source ? 1 - Eps : Eps) * ((y == j ? Eps : 0) + (j == h ? 1 - Eps : 0));
        return m;
    }

    // The oracle: sums the weight of every trajectory x_{-1}, x_0, ..., x_{n-1}, with no recursion shared with the code.
    // Predictive[t] adds the weight of the evidence before step t on x_{t-1}. Every prefix is counted once per suffix,
    // the same k^(n-t) times for all of them, so the factor cancels when the row is normalised.
    private static (double[][] Predictive, double[][] Smoothed) Enumerate(double[] prior, ChainStep[] steps)
    {
        int k = prior.Length, n = steps.Length;
        var predictive = Enumerable.Range(0, n).Select(_ => new double[k]).ToArray();
        var smoothed = Enumerable.Range(0, n).Select(_ => new double[k]).ToArray();
        var path = new int[n + 1];   // path[0] = state before step 0, path[t + 1] = state after step t
        for (var code = 0; code < (int)Math.Pow(k, n + 1); code++)
        {
            for (int d = 0, c = code; d <= n; d++, c /= k) path[d] = c % k;
            var w = prior[path[0]];
            for (var t = 0; t < n; t++)
            {
                predictive[t][path[t]] += w;
                var (from, to) = (path[t], path[t + 1]);
                w *= (steps[t].Kernel?[from, to] ?? (from == to ? 1 : 0)) * (steps[t].Emission?[to] ?? 1);
            }
            for (var t = 0; t < n; t++) smoothed[t][path[t + 1]] += w;
        }
        foreach (var row in predictive.Concat(smoothed))
        {
            var sum = row.Sum();
            for (var j = 0; j < k; j++) row[j] /= sum;
        }
        return (predictive, smoothed);
    }

    [Fact]
    public void Matches_a_brute_force_enumeration_of_every_trajectory()
    {
        double[] prior = [0.5, 0.3, 0.2];
        ChainStep[] steps =
        [
            new(null, [0.7, 0.2, 0.1]),              // a weak sighting, no jump
            new(Move(3, source: 0, h: 2), null),     // a named move 0 → 2: weighs its source, then jumps
            new(Move(3, source: null, h: 1), null),  // a named piece of evidence in 1: a plain jump
            new(null, [0.1, 0.3, 0.6]),              // another weak sighting
        ];

        var (predictive, smoothed) = ObjectChain.Run(prior, steps);
        var (expectedPredictive, expectedSmoothed) = Enumerate(prior, steps);

        for (var t = 0; t < steps.Length; t++)
            for (var j = 0; j < prior.Length; j++)
            {
                Assert.Equal(expectedPredictive[t][j], predictive[t][j], 1e-12);
                Assert.Equal(expectedSmoothed[t][j], smoothed[t][j], 1e-12);
            }
    }

    [Fact]
    public void The_predictive_of_step_zero_is_the_prior()
    {
        double[] prior = [0.5, 0.25, 0.25];

        var (predictive, _) = ObjectChain.Run(prior, [new(Move(3, source: 1, h: 0), [0.2, 0.3, 0.5])]);

        Assert.Equal(prior, predictive[0]);
    }

    [Fact]
    public void Stays_normalised_over_a_thousand_steps()
    {
        const int k = 4;
        var random = new Random(7);
        var steps = Enumerable.Range(0, 1000).Select(t => (t % 3) switch
        {
            0 => new ChainStep(Move(k, random.Next(k), random.Next(k)), null),
            1 => new ChainStep(Move(k, null, random.Next(k)), null),
            _ => new ChainStep(null, [.. Enumerable.Range(0, k).Select(_ => 0.01 + random.NextDouble())]),
        }).ToArray();

        var (predictive, smoothed) = ObjectChain.Run([0.25, 0.25, 0.25, 0.25], steps);

        Assert.Equal(1000, predictive.Length);
        Assert.Equal(1000, smoothed.Length);
        Assert.All(predictive.Concat(smoothed), row => Assert.Equal(1, row.Sum(), 1e-9));
    }

    [Fact]
    public void Contradictory_evidence_throws()
    {
        ChainStep[] steps = [new(null, [1, 0, 0]), new(null, [0, 1, 1])];

        var error = Assert.Throws<InvalidOperationException>(() => ObjectChain.Run([0.5, 0.25, 0.25], steps));

        Assert.Contains("step 1", error.Message);
    }
}
