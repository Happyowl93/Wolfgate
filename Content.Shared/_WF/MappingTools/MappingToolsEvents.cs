using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingTools;

/// <summary>
/// A mapping-tools selection on one grid: a tile area (right and top exclusive) and/or loose entities.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingSelection
{
    public NetEntity Grid;
    public Box2i? Area;
    public List<NetEntity> Entities = new();
}

/// <summary>
/// Moves the selection by whole tiles and quarter turns clockwise around its centre.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsMoveEvent(MappingSelection selection, Vector2i offset, int turns) : EntityEventArgs
{
    public readonly MappingSelection Selection = selection;
    public readonly Vector2i Offset = offset;
    public readonly int Turns = turns;
}

/// <summary>
/// Mirrors the selection in place, left-right or with <see cref="Vertical"/> top-bottom.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsMirrorEvent(MappingSelection selection, bool vertical) : EntityEventArgs
{
    public readonly MappingSelection Selection = selection;
    public readonly bool Vertical = vertical;
}

/// <summary>
/// Copies the selection to the sender's clipboard, deleting it when <see cref="Cut"/> is set.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsCopyEvent(MappingSelection selection, bool cut) : EntityEventArgs
{
    public readonly MappingSelection Selection = selection;
    public readonly bool Cut = cut;
}

/// <summary>
/// Pastes the sender's clipboard centred on <see cref="Target"/>, turned clockwise by <see cref="Turns"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsPasteEvent(NetCoordinates target, int turns) : EntityEventArgs
{
    public readonly NetCoordinates Target = target;
    public readonly int Turns = turns;
}

/// <summary>
/// Deletes the selection, tiles included.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsDeleteEvent(MappingSelection selection) : EntityEventArgs
{
    public readonly MappingSelection Selection = selection;
}

/// <summary>
/// Undoes (or with <see cref="Redo"/>, redoes) the sender's last mapping edit.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsHistoryEvent(bool redo) : EntityEventArgs
{
    public readonly bool Redo = redo;
}

/// <summary>
/// Server to client: what the clipboard holds, for the paste preview. Positions are relative to its bottom-left.
/// A paste off every grid lands on <see cref="SourceGrid"/>, the grid it was copied from.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsClipboardEvent(
    NetEntity sourceGrid,
    Vector2i size,
    List<Vector2i> tiles,
    List<MappingClipEntity> entities)
    : EntityEventArgs
{
    public readonly NetEntity SourceGrid = sourceGrid;
    public readonly Vector2i Size = size;
    public readonly List<Vector2i> Tiles = tiles;
    public readonly List<MappingClipEntity> Entities = entities;
}

/// <summary>
/// Server to client: replaces the client's selection, sent after a paste.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsSelectEvent(MappingSelection selection) : EntityEventArgs
{
    public readonly MappingSelection Selection = selection;
}

/// <summary>
/// One root entity in the clipboard preview.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct MappingClipEntity(string? Prototype, Vector2 Position, Angle Rotation);
