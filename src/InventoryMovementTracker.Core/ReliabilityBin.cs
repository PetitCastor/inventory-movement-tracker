namespace InventoryMovementTracker.Core;

/// <summary>One of the ten equal-width classes of a reliability diagram; <paramref name="Judged"/> when it holds at least 10 pairs.</summary>
public sealed record ReliabilityBin(int Count, double MeanP, double Frequency, bool Judged);
