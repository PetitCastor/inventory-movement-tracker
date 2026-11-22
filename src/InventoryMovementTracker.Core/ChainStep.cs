namespace InventoryMovementTracker.Core;

/// <summary>
/// One step of an <see cref="ObjectChain"/>: the kernel moves the state, then the emission weighs the new state.
/// Kernel[i, j] weighs going from state i to state j. It need not be normalised, since it may carry evidence
/// (the likelihood of a move's source); null is the identity, a step without a jump.
/// Emission[j] weighs state j after the kernel; null weighs every state alike.
/// </summary>
public sealed record ChainStep(double[,]? Kernel, double[]? Emission)
{
    /// <summary>
    /// P0.1 §4: M = diag(source likelihood) · J_target, with J_h[y, j] = ε·[y = j] + (1 − ε)·[j = h].
    /// The source likelihood is 1 − ε if the state is the source, else ε. No source: a plain jump. No target: no jump.
    /// </summary>
    public static ChainStep Move(int k, int? source, int? target, double epsilon)
    {
        var m = new double[k, k];
        for (var y = 0; y < k; y++)
            for (var j = 0; j < k; j++)
                m[y, j] = (source is null ? 1 : y == source ? 1 - epsilon : epsilon)
                    * (target is null ? (y == j ? 1 : 0) : (y == j ? epsilon : 0) + (j == target ? 1 - epsilon : 0));
        return new ChainStep(m, null);
    }
}
