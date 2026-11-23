namespace InventoryMovementTracker.Core;

/// <summary>
/// Continuous hazards between two proofs (P0.1 §3), in h⁻¹: an object leaves a holder for Out of sight with
/// P = 1 − exp(−λ·Δ). Consumption: Drink_, Food_ and crlf_consumable_ classes away from a station; Despawn: on the
/// floor; Leak: everywhere else. Rho: the chance to stay in a holder across a game update without a reset (§3, §7).
/// </summary>
public sealed record Hazards(double Consumption, double Despawn, double Leak, double Rho = 0.9)
{
    /// <summary>Declared choices (half-lives of about 17 h, 42 min and 8 months), to be tuned against the corpus.</summary>
    public static Hazards Default { get; } = new(1.0 / 24, 1, 1.0 / 8760);
}
