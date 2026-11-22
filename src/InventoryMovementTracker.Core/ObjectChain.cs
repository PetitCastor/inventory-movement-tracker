namespace InventoryMovementTracker.Core;

/// <summary>
/// One object's discrete hidden Markov chain over K states, solved by a hand-written forward-backward that
/// rescales both passes at every step (decision B1). O(n·K²): the caller keeps K small.
/// </summary>
public static class ObjectChain
{
    /// <param name="prior">The law of the state before step 0.</param>
    /// <returns>
    /// Predictive[t]: the law of the state just before step t given the evidence of the steps before t, the prior
    /// for t = 0. It leaves out step t's kernel, because that kernel already carries evidence (a move's source).
    /// Smoothed[t]: the law of the state after step t given all the evidence. Its last row is the final filter.
    /// </returns>
    /// <exception cref="InvalidOperationException">The evidence is contradictory: no state stays possible.</exception>
    public static (double[][] Predictive, double[][] Smoothed) Run(double[] prior, IReadOnlyList<ChainStep> steps)
    {
        int k = prior.Length, n = steps.Count;
        var predictive = new double[n][];
        var filtered = new double[n][];
        var law = Normalise([.. prior], "in the prior");
        for (var t = 0; t < n; t++)
        {
            predictive[t] = law;
            var next = new double[k];
            for (var j = 0; j < k; j++)
            {
                for (var i = 0; i < k; i++) next[j] += law[i] * Kernel(steps[t], i, j);
                next[j] *= Emission(steps[t], j);
            }
            law = filtered[t] = Normalise(next, $"at step {t}");
        }

        var smoothed = new double[n][];
        var beta = Enumerable.Repeat(1.0, k).ToArray();   // ∝ P(evidence after step t | state after step t)
        for (var t = n - 1; t >= 0; t--)
        {
            smoothed[t] = Normalise([.. filtered[t].Select((a, i) => a * beta[i])], $"at step {t}");
            var back = new double[k];
            for (var i = 0; i < k; i++)
                for (var j = 0; j < k; j++) back[i] += Kernel(steps[t], i, j) * Emission(steps[t], j) * beta[j];
            beta = Normalise(back, $"at step {t}");   // cannot be zero once the forward pass has found a possible state
        }
        return (predictive, smoothed);
    }

    private static double Kernel(ChainStep step, int from, int to) => step.Kernel?[from, to] ?? (from == to ? 1 : 0);

    private static double Emission(ChainStep step, int state) => step.Emission?[state] ?? 1;

    private static double[] Normalise(double[] weights, string where)
    {
        var sum = weights.Sum();
        if (!(sum > 0))
            throw new InvalidOperationException($"Contradictory evidence {where}: no state keeps a non-zero probability.");
        for (var i = 0; i < weights.Length; i++) weights[i] /= sum;
        return weights;
    }
}
