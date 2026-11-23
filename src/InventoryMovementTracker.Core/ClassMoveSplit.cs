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
    /// Shares a move of n units among places taken in order, the first <paramref name="sPlaces"/> being the source's: a
    /// geid's place offers 0 or 1 unit, a lot's place (no geid) a law of units. Each unit the source lacks is born with
    /// probability <paramref name="birth"/>, independently; the later places are asked for the others only, and what they
    /// lack is born too. A birth is drawn before them, so the share of births exceeds <paramref name="birth"/>. Geids and
    /// lots are taken as independent; a geid's places exclude one another, so it counts once, at the source, at a later
    /// place or elsewhere, and it is left out of the demand on its own places.
    /// </summary>
    /// <returns>
    /// R: per geid place, P(the places before it, its own geid left out, leave a unit wanted there). Lots: per lot place,
    /// the laws of the units taken and left. Short[k]: P(k units are born), or, at birth 0, still wanted after every place.
    /// </returns>
    public static (double[] R, (double[] Taken, double[] Left)[] Lots, double[] Short) Supply(
        IReadOnlyList<(string? Geid, double[] Offer)> places, int n, int sPlaces = 0, double birth = 0)
    {
        // Joint laws of the units offered so far, by the source (row) and by the later places (column); rank n means n or more.
        double[,] Add(double[,] law, IEnumerable<(int S, int Later, double P)> offer)
        {
            var sum = new double[n + 1, n + 1];
            foreach (var (s, later, p) in offer)
                for (var a = 0; a <= n; a++)
                    for (var b = 0; b <= n; b++) sum[Math.Min(n, a + s), Math.Min(n, b + later)] += law[a, b] * p;
            return sum;
        }
        var offered = new double[n + 1, n + 1];   // by the lots so far
        offered[0, 0] = 1;
        var present = new Dictionary<string, (double S, double Later)>();   // P(the geid is at one of the places so far)

        // The law of `units`, a function of the units the source lacks, those of them not born, and the later places' offer.
        double[] Law(string? except, double born, Func<int, int, int, int> units)
        {
            var joint = present.Where(p => p.Key != except).Select(p => p.Value).Aggregate(offered, (law, p) =>
                Add(law, [(0, 0, Math.Max(0, 1 - p.S - p.Later)), (1, 0, p.S), (0, 1, p.Later)]));   // the clamps absorb rounding
            var result = new double[n + 1];
            double[] asked = [1];   // the law of the units not born among the `lack`
            for (var lack = 0; lack <= n; lack++, asked = LotLaw.Add(asked, [born, 1 - born]))
                for (var later = 0; later <= n; later++)
                    for (var k = 0; k <= lack; k++) result[units(lack, k, later)] += joint[n - lack, later] * asked[k];
            return result;
        }

        var r = new List<double>();
        var lots = new List<(double[], double[])>();
        for (var i = 0; i < places.Count; i++)
        {
            var (geid, offer) = places[i];
            var atS = i < sPlaces;
            var wanted = Law(geid, atS ? 0 : birth, (_, k, later) => Math.Max(0, k - later));   // nothing is born before the source has served
            if (geid is null)
            {
                lots.Add(LotLaw.Take(wanted, offer));
                offered = Add(offered, offer.Select((p, k) => atS ? (k, 0, p) : (0, k, p)));
            }
            else
            {
                r.Add(wanted[0] == 0 ? 1 : Math.Min(1, wanted.Skip(1).Sum()));   // a null or certain r stays exactly so
                var (s, later) = present.GetValueOrDefault(geid);
                present[geid] = atS ? (s + offer[1], later) : (s, later + offer[1]);
            }
        }
        return ([.. r], [.. lots], Law(null, birth, (lack, k, later) => lack - Math.Min(k, later)));
    }
}
