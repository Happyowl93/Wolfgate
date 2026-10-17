using System.Numerics;
using Content.Shared.Decals;
using Robust.Shared.Map;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Timing;

namespace Content.Server._WF.MappingTools;

/// <summary>
/// A handle to an entity that stays valid when undo deletes and recreates it under a new uid.
/// </summary>
public sealed class MappingEntityRef(EntityUid uid)
{
    public EntityUid Uid = uid;
}

/// <summary>
/// Where a root entity sits: its parent (a grid, or a map for loose entities), local position and rotation.
/// </summary>
public record struct MappingPose(EntityUid Parent, Vector2 Position, Angle Rotation, bool Anchored);

/// <summary>
/// Root entities saved with the engine serializer so they can be deleted and recreated with their contents.
/// </summary>
public sealed class MappingEntityGroup
{
    /// <summary>
    /// The saved entities, or null while they exist and haven't been saved yet.
    /// </summary>
    public MappingDataNode? Data;

    public readonly List<MappingGroupRoot> Roots = new();
}

/// <summary>
/// One root of a <see cref="MappingEntityGroup"/>, matched to its saved data by yaml uid.
/// </summary>
public sealed class MappingGroupRoot(MappingEntityRef entity, int yamlUid, MappingPose pose)
{
    public readonly MappingEntityRef Entity = entity;
    public int YamlUid = yamlUid;
    public MappingPose Pose = pose;
}

public record struct MappingTileChange(EntityUid Grid, Vector2i Indices, Tile Old, Tile New);

public record struct MappingEntityMove(MappingEntityRef Entity, MappingPose Old, MappingPose New);

/// <summary>
/// A decal by value, with grid-local coordinates; decal ids change when one is re-added.
/// </summary>
public record struct MappingDecal(EntityUid Grid, Decal Decal);

/// <summary>
/// One undoable change. Applying it forward goes from the old state to the new one, backward the reverse.
/// </summary>
public sealed class MappingEdit(string name)
{
    /// <summary>
    /// Fluent id naming the edit in the undo and redo popups.
    /// </summary>
    public readonly string Name = name;

    /// <summary>
    /// The tick a placement edit was recorded on; placements in the same tick merge into one edit.
    /// </summary>
    public GameTick? PlacementTick;

    public readonly List<MappingTileChange> Tiles = new();
    public readonly List<MappingEntityMove> Moves = new();
    public readonly List<MappingEntityGroup> Created = new();
    public readonly List<MappingEntityGroup> Deleted = new();
    public readonly List<MappingDecal> DecalsRemoved = new();
    public readonly List<MappingDecal> DecalsAdded = new();

    public bool IsEmpty => Tiles.Count == 0 && Moves.Count == 0 && Created.Count == 0 && Deleted.Count == 0 &&
                           DecalsRemoved.Count == 0 && DecalsAdded.Count == 0;

    /// <summary>
    /// Appends another edit that happened after this one.
    /// </summary>
    public void Merge(MappingEdit other)
    {
        Tiles.AddRange(other.Tiles);
        Moves.AddRange(other.Moves);
        Created.AddRange(other.Created);
        Deleted.AddRange(other.Deleted);
        DecalsRemoved.AddRange(other.DecalsRemoved);
        DecalsAdded.AddRange(other.DecalsAdded);
    }
}

/// <summary>
/// A copied selection. Positions are relative to the bottom-left of a box of <see cref="Size"/>.
/// </summary>
public sealed class MappingClipboard
{
    /// <summary>
    /// The grid it was copied from, which a paste off every grid lands on.
    /// </summary>
    public EntityUid SourceGrid;

    public Vector2i Size;
    public readonly List<(Vector2i Cell, Tile Tile)> Tiles = new();
    public readonly List<Decal> Decals = new();
    public MappingDataNode? Data;
    public readonly List<(int YamlUid, MappingPose Pose)> Roots = new();
}
