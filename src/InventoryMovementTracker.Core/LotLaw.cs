namespace InventoryMovementTracker.Core;

/// <summary>Laws of a number of units (P0.1 §6): the index is the count, the masses sum to 1.</summary>
public static class LotLaw
{
    /// <returns>The laws of Taken = min(demand, lot) and Left = lot − Taken, for a demand independent of the lot.</returns>
    public static (double[] Taken, double[] Left) Take(double[] demand, double[] lot)
    {
        var taken = new double[Math.Min(demand.Length, lot.Length)];
        var left = new double[lot.Length];
        for (var k = 0; k < demand.Length; k++)
            for (var l = 0; l < lot.Length; l++)
            {
                taken[Math.Min(k, l)] += demand[k] * lot[l];
                left[Math.Max(0, l - k)] += demand[k] * lot[l];
            }
        return (taken, left);
    }

    /// <returns>The law of the sum of two independent counts: their convolution.</returns>
    public static double[] Add(double[] a, double[] b) => Combine(a, b, (i, j) => i + j);

    /// <returns>The law of the larger of two independent counts.</returns>
    public static double[] Max(double[] a, double[] b) => Combine(a, b, Math.Max);

    /// <returns>The law of the units kept when each is kept with probability <paramref name="keep"/>, independently.</returns>
    public static double[] Thin(double[] law, double keep)
    {
        var kept = new double[law.Length];
        double[] among = [1];   // the law of the units kept among n
        for (var n = 0; n < law.Length; n++, among = Add(among, [1 - keep, keep]))
            for (var k = 0; k <= n; k++) kept[k] += law[n] * among[k];
        return kept;
    }

    // The law of a function, increasing in both, of two independent counts.
    private static double[] Combine(double[] a, double[] b, Func<int, int, int> of)
    {
        var law = new double[of(a.Length - 1, b.Length - 1) + 1];
        for (var i = 0; i < a.Length; i++)
            for (var j = 0; j < b.Length; j++) law[of(i, j)] += a[i] * b[j];
        return law;
    }
}
