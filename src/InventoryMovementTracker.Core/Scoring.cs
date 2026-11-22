namespace InventoryMovementTracker.Core;

/// <summary>Domain-free scores over (probability, 0/1 outcome) pairs. Every probability is clipped to [1e-3, 1 - 1e-3] first.</summary>
public static class Scoring
{
    private const double Eps = 1e-3;

    private static double Clip(double p) => Math.Clamp(p, Eps, 1 - Eps);

    public static double Brier(IEnumerable<(double P, int Y)> pairs) =>
        pairs.Average(x => Math.Pow(Clip(x.P) - x.Y, 2));

    public static double LogLoss(IEnumerable<(double P, int Y)> pairs) =>
        pairs.Average(x => -Math.Log(x.Y == 1 ? Clip(x.P) : 1 - Clip(x.P)));

    public static IReadOnlyList<ReliabilityBin> Reliability(IEnumerable<(double P, int Y)> pairs)
    {
        var byBin = pairs.ToLookup(x => Math.Min((int)(Clip(x.P) * 10), 9));
        return Enumerable.Range(0, 10).Select(i =>
        {
            var g = byBin[i].ToArray();
            return g.Length == 0
                ? new ReliabilityBin(0, 0, 0, false)
                : new ReliabilityBin(g.Length, g.Average(x => Clip(x.P)), g.Average(x => (double)x.Y), g.Length >= 10);
        }).ToArray();
    }

    /// <summary>95% interval (percentiles 2.5 and 97.5) of the mean of lossA - lossB, resampling checks by index.</summary>
    public static (double Low, double High) PairedBootstrap(IReadOnlyList<double> lossA, IReadOnlyList<double> lossB, int draws = 10_000, int seed = 12345)
    {
        var diff = lossA.Zip(lossB, (a, b) => a - b).ToArray();
        var rng = new Random(seed);
        var means = new double[draws];
        for (var d = 0; d < draws; d++)
        {
            var sum = 0.0;
            for (var i = 0; i < diff.Length; i++) sum += diff[rng.Next(diff.Length)];
            means[d] = sum / diff.Length;
        }
        Array.Sort(means);
        return (means[(int)Math.Round(0.025 * (draws - 1))], means[(int)Math.Round(0.975 * (draws - 1))]);
    }

    /// <summary>Splits by time order, never shuffled: the earliest <paramref name="trainFraction"/> trains, the rest tests.</summary>
    public static (IReadOnlyList<T> Train, IReadOnlyList<T> Test) Holdout<T>(IEnumerable<T> items, Func<T, DateTimeOffset> time, double trainFraction)
    {
        var ordered = items.OrderBy(time).ToArray();
        var cut = (int)Math.Floor(ordered.Length * trainFraction);
        return (ordered[..cut], ordered[cut..]);
    }
}
