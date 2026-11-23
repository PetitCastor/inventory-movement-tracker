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

    /// <summary>
    /// Shares a move of n units among places taken in order: a geid's place offers 0 or 1 unit, a lot's place (no geid) a
    /// law of units. Geids and lots are taken as independent; a geid's places exclude one another, so it counts once, with
    /// the sum of its masses, and it is left out of the demand on its own places.
    /// </summary>
    /// <returns>
    /// R: per geid place, P(the places before it, its own geid left out, offer fewer than n units). Lots: per lot place,
    /// the laws of the units taken and left. Short[k]: P(k units are still wanted after every place).
    /// </returns>
    public static (double[] R, (double[] Taken, double[] Left)[] Lots, double[] Short) Supply(
        IReadOnlyList<(string? Geid, double[] Offer)> places, int n)
    {
        double[] Cap(double[] law) => law.Length <= n ? law : [.. law[..n], law[n..].Sum()];   // rank n means n or more
        double[] offered = [1, .. new double[n]];   // by the lots so far
        var present = new Dictionary<string, double>();   // P(the geid is at one of the places so far)
        double[] Wanted(string? except) => [.. present.Where(p => p.Key != except)
            .Aggregate(offered, (law, p) => Cap(LotLaw.Add(law, [1 - p.Value, p.Value]))).Reverse()];
        var r = new List<double>();
        var lots = new List<(double[], double[])>();
        foreach (var (geid, offer) in places)
        {
            var wanted = Wanted(except: geid);
            if (geid is null)
            {
                lots.Add(LotLaw.Take(wanted, offer));
                offered = Cap(LotLaw.Add(offered, Cap(offer)));
            }
            else
            {
                r.Add(Math.Max(0, 1 - wanted[0]));   // the clamps absorb rounding
                present[geid] = Math.Min(1, present.GetValueOrDefault(geid) + offer[1]);
            }
        }
        return ([.. r], [.. lots], Wanted(except: null));
    }
}
