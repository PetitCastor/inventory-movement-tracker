namespace InventoryMovementTracker.Core;

/// <summary>
/// Domain-free split of a move of n units of one class out of a source (P0.1 §6). Candidates arrive in priority
/// order; each is present with its own probability, independently. Units are taken in that order from the present ones.
/// </summary>
public static class ClassMoveSplit
{
    /// <param name="presence">P(candidate i is at the source), best candidate first.</param>
    /// <returns>
    /// Moved[i]: P(candidate i moved) = presence[i] · P(fewer than n present candidates before it).
    /// Shortfall[k]: P(k units were not supplied by these candidates), k = 0..n; the other §6 candidates cover them.
    /// </returns>
    public static (double[] Moved, double[] Shortfall) Split(IReadOnlyList<double> presence, int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        var moved = new double[presence.Count];
        var count = new double[n + 1];   // Poisson-binomial law of the present candidates so far; count[n] means n or more
        count[0] = 1;
        for (var i = 0; i < presence.Count; i++)
        {
            var p = presence[i];
            moved[i] = p * (1 - count[n]);
            count[n] += count[n - 1] * p;
            for (var j = n - 1; j > 0; j--) count[j] = count[j] * (1 - p) + count[j - 1] * p;
            count[0] *= 1 - p;
        }
        return (moved, count.Reverse().ToArray());
    }
}
