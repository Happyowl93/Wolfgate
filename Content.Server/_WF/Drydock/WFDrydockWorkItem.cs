using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Server._WF.Drydock;

/// <summary>What a drydock work item does to the ship.</summary>
public enum WFDrydockWorkKind : byte
{
    Tile,
    Entity,
    Heal,
    Upgrade,
}

/// <summary>
/// One unit of drydock work: a hull tile, a missing entity, a heal or a refit tier.
/// </summary>
public sealed class WFDrydockWorkItem
{
    /// <summary>What this item does.</summary>
    public WFDrydockWorkKind Kind;

    /// <summary>Ship grid index of a hull tile.</summary>
    public Vector2i TileIndex;

    /// <summary>Stored tile type to restore.</summary>
    public int TileType;

    /// <summary>Repair snapshot chunk the item belongs to.</summary>
    public Vector2i ChunkPos;

    /// <summary>Key of the entity spec in its chunk.</summary>
    public int SpecId;

    /// <summary>Entity to heal or refit, or the original entity of a missing one.</summary>
    public EntityUid Target;

    /// <summary>Upgrade prototype id for refits.</summary>
    public string? Upgrade;

    /// <summary>Tier a refit raises the part to.</summary>
    public int Tier;

    /// <summary>Ship-local position used for effects and crane targeting.</summary>
    public Vector2 LocalPos;

    /// <summary>Credits charged for this item.</summary>
    public int Cost;

    /// <summary>Material bill when quoted by a console that pays in materials, or null.</summary>
    public Dictionary<string, int>? Materials;

    /// <summary>Seconds one crane spends on this item, set when the job starts.</summary>
    public float WorkTime;
}
