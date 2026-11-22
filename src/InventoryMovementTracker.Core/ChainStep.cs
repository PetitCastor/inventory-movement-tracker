namespace InventoryMovementTracker.Core;

/// <summary>
/// One step of an <see cref="ObjectChain"/>: the kernel moves the state, then the emission weighs the new state.
/// Kernel[i, j] weighs going from state i to state j. It need not be normalised, since it may carry evidence
/// (the likelihood of a move's source); null is the identity, a step without a jump.
/// Emission[j] weighs state j after the kernel; null weighs every state alike.
/// </summary>
public sealed record ChainStep(double[,]? Kernel, double[]? Emission);
