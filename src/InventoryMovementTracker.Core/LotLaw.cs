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
    public static double[] Add(double[] a, double[] b)
    {
        var sum = new double[a.Length + b.Length - 1];
        for (var i = 0; i < a.Length; i++)
            for (var j = 0; j < b.Length; j++) sum[i + j] += a[i] * b[j];
        return sum;
    }
}
