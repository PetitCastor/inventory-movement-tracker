namespace InventoryMovementTracker.Core;

/// <summary>A direct holder of an object, as the old app's (kind, key) columns name it.</summary>
public sealed record Holder(string Kind, string Key)
{
    /// <summary>The player's body: the old app's <c>Equipped</c>, whose key is always empty.</summary>
    public static readonly Holder Body = new("Equipped", "");

    /// <summary>Lost from view, used or consumed (P0.1 §2). Not absorbing: any named evidence brings the object back.</summary>
    public static readonly Holder OutOfSight = new("OutOfSight", "");
}
