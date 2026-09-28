using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Decals;
using Content.Server.Popups;
using Content.Shared.NodeContainer;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Decals;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingTools;

/// <summary>
/// Server side of the mapping tools: moves, copies, pastes and deletes selections, and keeps each mapper's
/// undo history, including their placement-menu edits.
/// </summary>
public sealed partial class MappingToolsSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IDependencyCollection _dependency = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private TransformSystem _transform = default!;

    /// <summary>
    /// Set while this system changes the world itself, so its own tile changes aren't recorded as placements.
    /// </summary>
    private bool _applying;

    /// <summary>
    /// Shared refs, so every edit that touches an entity follows it when undo recreates it.
    /// </summary>
    private readonly Dictionary<EntityUid, MappingEntityRef> _refs = new();

    private readonly Dictionary<ICommonSession, MappingClipboard> _clipboards = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MappingToolsMoveEvent>(OnMove);
        SubscribeNetworkEvent<MappingToolsCopyEvent>(OnCopy);
        SubscribeNetworkEvent<MappingToolsPasteEvent>(OnPaste);
        SubscribeNetworkEvent<MappingToolsDeleteEvent>(OnDelete);
        SubscribeNetworkEvent<MappingToolsMirrorEvent>(OnMirror);
        SubscribeNetworkEvent<MappingToolsHistoryEvent>(OnHistory);

        InitializeHistory();
    }

    private bool CanUse(ICommonSession session)
    {
        return _admin.HasAdminFlag(session, AdminFlags.Mapping);
    }

    private MappingEntityRef GetRef(EntityUid uid)
    {
        if (!_refs.TryGetValue(uid, out var entityRef))
        {
            entityRef = new MappingEntityRef(uid);
            _refs[uid] = entityRef;
        }

        return entityRef;
    }

    #region Selection

    /// <summary>
    /// A selection resolved on the server: its grid, the tiles and decals in its area and its root entities.
    /// </summary>
    private sealed class Resolved
    {
        public Entity<MapGridComponent> Grid;
        public Box2i Extent;
        public readonly List<(Vector2i Cell, Tile Tile)> Tiles = new();
        public readonly List<Decal> Decals = new();
        public readonly HashSet<EntityUid> Entities = new();
    }

    private bool TryResolve(MappingSelection selection, [NotNullWhen(true)] out Resolved? resolved)
    {
        resolved = null;
        var gridUid = GetEntity(selection.Grid);
        if (!TryComp(gridUid, out MapGridComponent? grid) || TerminatingOrDeleted(gridUid))
            return false;

        var result = new Resolved { Grid = (gridUid, grid) };
        Box2i? extent = null;

        if (selection.Area is { } area && area.Width > 0 && area.Height > 0 && area.Area <= MaxArea)
        {
            extent = area;

            for (var x = area.Left; x < area.Right; x++)
            {
                for (var y = area.Bottom; y < area.Top; y++)
                {
                    var cell = new Vector2i(x, y);
                    if (_map.TryGetTileRef(gridUid, grid, cell, out var tileRef) && !tileRef.Tile.IsEmpty)
                        result.Tiles.Add((cell, tileRef.Tile));
                }
            }

            var children = Transform(gridUid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (MappingToolsMath.Contains(area, MappingToolsMath.CellOf(Transform(child).LocalPosition)) &&
                    IsSelectable(child))
                {
                    result.Entities.Add(child);
                }
            }

            var bounds = new Box2(area.Left - 1, area.Bottom - 1, area.Right + 1, area.Top + 1);
            foreach (var (_, decal) in _decals.GetDecalsIntersecting(gridUid, bounds))
            {
                if (MappingToolsMath.Contains(area, MappingToolsMath.CellOf(decal.Coordinates + new Vector2(0.5f))))
                    result.Decals.Add(decal);
            }
        }

        foreach (var netEntity in selection.Entities)
        {
            if (!TryGetEntity(netEntity, out var uid) ||
                Transform(uid.Value).ParentUid != gridUid ||
                !IsSelectable(uid.Value))
            {
                continue;
            }

            result.Entities.Add(uid.Value);
            extent = MappingToolsMath.Include(extent, MappingToolsMath.CellOf(Transform(uid.Value).LocalPosition));
        }

        if (extent is not { } finalExtent)
            return false;

        result.Extent = finalExtent;
        resolved = result;
        return true;
    }

    /// <summary>
    /// Whether an entity can be moved, copied or deleted: not a grid, map, player or ghost, and not holding a player.
    /// </summary>
    private bool IsSelectable(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || HasComp<MapGridComponent>(uid) || HasComp<MapComponent>(uid) ||
            HasComp<GhostComponent>(uid))
        {
            return false;
        }

        var queue = new List<EntityUid> { uid };
        for (var i = 0; i < queue.Count; i++)
        {
            if (HasComp<ActorComponent>(queue[i]))
                return false;

            var children = Transform(queue[i]).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                queue.Add(child);
            }
        }

        return true;
    }

    /// <summary>
    /// Largest area, in tiles, one request may touch.
    /// </summary>
    private const int MaxArea = 256 * 256;

    #endregion

    #region Requests

    private void OnMove(MappingToolsMoveEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession))
            Move(args.SenderSession, ev.Selection, ev.Offset, ev.Turns);
    }

    private void OnCopy(MappingToolsCopyEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession))
            Copy(args.SenderSession, ev.Selection, ev.Cut);
    }

    private void OnPaste(MappingToolsPasteEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession))
            Paste(args.SenderSession, GetCoordinates(ev.Target), ev.Turns);
    }

    private void OnMirror(MappingToolsMirrorEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession))
            Mirror(args.SenderSession, ev.Selection, ev.Vertical);
    }

    private void OnDelete(MappingToolsDeleteEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession))
            Delete(args.SenderSession, ev.Selection);
    }

    private void OnHistory(MappingToolsHistoryEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession))
            StepHistory(args.SenderSession, ev.Redo);
    }

    /// <summary>
    /// Moves a selection by whole tiles and quarter turns clockwise, as one undoable edit.
    /// </summary>
    public void Move(ICommonSession session, MappingSelection selection, Vector2i offset, int turns)
    {
        if (!TryResolve(selection, out var sel))
            return;

        turns = MappingToolsMath.Normalize(turns);
        if (offset == Vector2i.Zero && turns == 0)
            return;

        var edit = TransformEdit(sel,
            turns == 0 ? "wf-mapping-tools-edit-move" : "wf-mapping-tools-edit-rotate",
            cell => MappingToolsMath.TransformCell(cell, sel.Extent, offset, turns),
            point => MappingToolsMath.TransformPoint(point, sel.Extent, offset, turns),
            (_, rotation) => rotation + MappingToolsMath.RotationDelta(turns),
            tile => RotateTile(tile, turns),
            decal => MoveDecal(decal, sel.Extent, offset, turns));

        Commit(session, edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to move {sel.Entities.Count} entities and {sel.Tiles.Count} tiles on {ToPrettyString(sel.Grid.Owner):grid} by {offset}, {turns} turns");
    }

    /// <summary>
    /// Mirrors a selection in place, left-right or with <paramref name="vertical"/> top-bottom, as one undoable edit.
    /// </summary>
    public void Mirror(ICommonSession session, MappingSelection selection, bool vertical)
    {
        if (!TryResolve(selection, out var sel))
            return;

        var edit = TransformEdit(sel, "wf-mapping-tools-edit-mirror",
            cell => MappingToolsMath.MirrorCell(cell, sel.Extent, vertical),
            point => MappingToolsMath.MirrorPoint(point, sel.Extent, vertical),
            (uid, rotation) => MirrorRotation(uid, rotation, vertical),
            tile => MirrorTile(tile, vertical),
            decal => MirrorDecal(decal, sel.Extent, vertical));

        Commit(session, edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to mirror {sel.Entities.Count} entities and {sel.Tiles.Count} tiles on {ToPrettyString(sel.Grid.Owner):grid}");
    }

    /// <summary>
    /// An edit that carries everything in a selection to new cells, positions and facings.
    /// </summary>
    private MappingEdit TransformEdit(
        Resolved sel,
        string name,
        Func<Vector2i, Vector2i> cellMap,
        Func<Vector2, Vector2> pointMap,
        Func<EntityUid, Angle, Angle> rotationMap,
        Func<Tile, Tile> tileMap,
        Func<Decal, Decal> decalMap)
    {
        var grid = sel.Grid.Owner;
        var edit = new MappingEdit(name);

        var newTiles = new Dictionary<Vector2i, Tile>();
        foreach (var (cell, tile) in sel.Tiles)
        {
            newTiles[cellMap(cell)] = tileMap(tile);
        }

        foreach (var (cell, _) in sel.Tiles)
        {
            if (!newTiles.ContainsKey(cell))
                edit.Tiles.Add(new MappingTileChange(grid, cell, TileAt(sel.Grid, cell), Tile.Empty));
        }

        foreach (var (cell, tile) in newTiles)
        {
            edit.Tiles.Add(new MappingTileChange(grid, cell, TileAt(sel.Grid, cell), tile));
        }

        foreach (var uid in sel.Entities)
        {
            var old = PoseOf(uid);
            var pose = old with
            {
                Position = pointMap(old.Position),
                Rotation = rotationMap(uid, old.Rotation),
            };
            edit.Moves.Add(new MappingEntityMove(GetRef(uid), old, pose));
        }

        foreach (var decal in sel.Decals)
        {
            edit.DecalsRemoved.Add(new MappingDecal(grid, decal));
            edit.DecalsAdded.Add(new MappingDecal(grid, decalMap(decal)));
        }

        return edit;
    }

    /// <summary>
    /// Copies a selection to the mapper's clipboard, deleting it as one undoable edit when cutting.
    /// </summary>
    public void Copy(ICommonSession session, MappingSelection selection, bool cut)
    {
        if (!TryResolve(selection, out var sel))
            return;

        var origin = sel.Extent.BottomLeft;
        var clip = new MappingClipboard { SourceGrid = sel.Grid.Owner, Size = sel.Extent.Size };

        foreach (var (cell, tile) in sel.Tiles)
        {
            clip.Tiles.Add((cell - origin, tile));
        }

        foreach (var decal in sel.Decals)
        {
            clip.Decals.Add(decal.WithCoordinates(decal.Coordinates - origin));
        }

        var preview = new List<MappingClipEntity>(sel.Entities.Count);
        if (sel.Entities.Count > 0)
        {
            var serializer = new EntitySerializer(_dependency, SnapshotOptions);
            serializer.SerializeEntityRecursive(sel.Entities);
            clip.Data = serializer.Write();

            foreach (var uid in sel.Entities)
            {
                var pose = PoseOf(uid) with { Parent = EntityUid.Invalid };
                pose.Position -= origin;
                clip.Roots.Add((serializer.YamlUidMap[uid], pose));
                preview.Add(new MappingClipEntity(MetaData(uid).EntityPrototype?.ID, pose.Position, pose.Rotation));
            }
        }

        _clipboards[session] = clip;
        SendClipboard(session, clip, preview);

        if (cut)
            Commit(session, DeleteEdit(sel, "wf-mapping-tools-edit-cut"));

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to {(cut ? "cut" : "copy")} {sel.Entities.Count} entities and {sel.Tiles.Count} tiles on {ToPrettyString(sel.Grid.Owner):grid}");
    }

    /// <summary>
    /// Pastes the mapper's clipboard centred on <paramref name="coords"/>, as one undoable edit.
    /// </summary>
    public void Paste(ICommonSession session, EntityCoordinates coords, int turns)
    {
        if (!_clipboards.TryGetValue(session, out var clip) || !coords.IsValid(EntityManager))
            return;

        var mapCoords = _transform.ToMapCoordinates(coords);
        if (!_mapManager.TryFindGridAt(mapCoords, out var gridUid, out var grid) &&
            !TryPasteGrid(clip.SourceGrid, mapCoords.MapId, out gridUid, out grid))
        {
            // Off every grid, with the source grid gone: start a new grid under the cursor.
            var newGrid = _mapManager.CreateGridEntity(mapCoords.MapId);
            _transform.SetWorldPosition(newGrid.Owner, new Vector2(MathF.Floor(mapCoords.X), MathF.Floor(mapCoords.Y)));
            (gridUid, grid) = (newGrid.Owner, newGrid.Comp);
        }

        turns = MappingToolsMath.Normalize(turns);
        var cursor = _map.TileIndicesFor(gridUid, grid, mapCoords);
        var extent = new Box2i(Vector2i.Zero, clip.Size);
        var offset = MappingToolsMath.PasteOrigin(cursor, clip.Size, turns) - MappingToolsMath.DestinationOrigin(extent, Vector2i.Zero, turns);

        var edit = new MappingEdit("wf-mapping-tools-edit-paste");
        foreach (var (cell, tile) in clip.Tiles)
        {
            var dest = MappingToolsMath.TransformCell(cell, extent, offset, turns);
            edit.Tiles.Add(new MappingTileChange(gridUid, dest, TileAt((gridUid, grid), dest), RotateTile(tile, turns)));
        }

        foreach (var decal in clip.Decals)
        {
            edit.DecalsAdded.Add(new MappingDecal(gridUid, MoveDecal(decal, extent, offset, turns)));
        }

        var group = new MappingEntityGroup { Data = clip.Data };
        foreach (var (yaml, pose) in clip.Roots)
        {
            var placed = pose with
            {
                Parent = gridUid,
                Position = MappingToolsMath.TransformPoint(pose.Position, extent, offset, turns),
                Rotation = pose.Rotation + MappingToolsMath.RotationDelta(turns),
            };
            group.Roots.Add(new MappingGroupRoot(new MappingEntityRef(EntityUid.Invalid), yaml, placed));
        }

        if (group.Roots.Count > 0)
            edit.Created.Add(group);

        Commit(session, edit);

        var selection = new MappingSelection
        {
            Grid = GetNetEntity(gridUid),
            Area = clip.Tiles.Count > 0 || clip.Decals.Count > 0
                ? MappingToolsMath.TransformBox(extent, extent, offset, turns)
                : null,
        };

        foreach (var root in group.Roots)
        {
            if (!TerminatingOrDeleted(root.Entity.Uid))
                selection.Entities.Add(GetNetEntity(root.Entity.Uid));
        }

        RaiseNetworkEvent(new MappingToolsSelectEvent(selection), session);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to paste {group.Roots.Count} entities and {clip.Tiles.Count} tiles on {ToPrettyString(gridUid):grid}");
    }

    /// <summary>
    /// Deletes a selection, tiles included, as one undoable edit.
    /// </summary>
    public void Delete(ICommonSession session, MappingSelection selection)
    {
        if (!TryResolve(selection, out var sel))
            return;

        Commit(session, DeleteEdit(sel, "wf-mapping-tools-edit-delete"));
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to delete {sel.Entities.Count} entities and {sel.Tiles.Count} tiles on {ToPrettyString(sel.Grid.Owner):grid}");
    }

    #endregion

    /// <summary>
    /// An edit that removes everything in a selection, tiles included.
    /// </summary>
    private MappingEdit DeleteEdit(Resolved sel, string name)
    {
        var grid = sel.Grid.Owner;
        var edit = new MappingEdit(name);

        foreach (var decal in sel.Decals)
        {
            edit.DecalsRemoved.Add(new MappingDecal(grid, decal));
        }

        if (sel.Entities.Count > 0)
        {
            var group = new MappingEntityGroup();
            foreach (var uid in sel.Entities)
            {
                group.Roots.Add(new MappingGroupRoot(GetRef(uid), -1, PoseOf(uid)));
            }

            edit.Deleted.Add(group);
        }

        foreach (var (cell, tile) in sel.Tiles)
        {
            edit.Tiles.Add(new MappingTileChange(grid, cell, tile, Tile.Empty));
        }

        return edit;
    }

    private void SendClipboard(ICommonSession session, MappingClipboard clip, List<MappingClipEntity> entities)
    {
        var tiles = new List<Vector2i>(clip.Tiles.Count);
        foreach (var (cell, _) in clip.Tiles)
        {
            tiles.Add(cell);
        }

        RaiseNetworkEvent(new MappingToolsClipboardEvent(GetNetEntity(clip.SourceGrid), clip.Size, tiles, entities), session);
    }

    /// <summary>
    /// The clipboard's source grid, if it still exists on the map being pasted on.
    /// </summary>
    private bool TryPasteGrid(EntityUid source, MapId map, out EntityUid gridUid, [NotNullWhen(true)] out MapGridComponent? grid)
    {
        gridUid = source;
        return TryComp(source, out grid) && !TerminatingOrDeleted(source) && Transform(source).MapID == map;
    }

    private Tile TileAt(Entity<MapGridComponent> grid, Vector2i cell)
    {
        return _map.TryGetTileRef(grid, grid, cell, out var tileRef) ? tileRef.Tile : Tile.Empty;
    }

    /// <summary>
    /// Turns a rotatable tile with its selection.
    /// </summary>
    private Tile RotateTile(Tile tile, int turns)
    {
        if (turns == 0 || tile.IsEmpty || !_tileDefs[tile.TypeId].AllowRotationMirror)
            return tile;

        return new Tile(tile.TypeId, tile.Flags, tile.Variant, MappingToolsMath.RotateTileState(tile.RotationMirroring, turns));
    }

    /// <summary>
    /// Mirrors a rotatable tile with its selection.
    /// </summary>
    private Tile MirrorTile(Tile tile, bool vertical)
    {
        if (tile.IsEmpty || !_tileDefs[tile.TypeId].AllowRotationMirror)
            return tile;

        return new Tile(tile.TypeId, tile.Flags, tile.Variant, MappingToolsMath.MirrorTileState(tile.RotationMirroring, vertical));
    }

    /// <summary>
    /// The facing a mirrored entity takes. Pipe fittings that aren't symmetric (bends, T-junctions, mixers) can't
    /// just flip their angle, so they take whichever quarter turn gives them the mirrored connections.
    /// </summary>
    private Angle MirrorRotation(EntityUid uid, Angle rotation, bool vertical)
    {
        var mirrored = MappingToolsMath.MirrorAngle(rotation, vertical);
        if (!TryComp(uid, out NodeContainerComponent? nodes))
            return mirrored;

        var pipes = new List<PipeNode>();
        foreach (var node in nodes.Nodes.Values)
        {
            if (node is PipeNode pipe)
                pipes.Add(pipe);
        }

        if (pipes.Count == 0)
            return mirrored;

        // The plain mirror goes first, so symmetric fittings keep the facing a mapper would expect.
        foreach (var candidate in new[] { mirrored, mirrored + Math.PI / 2, mirrored + Math.PI, mirrored - Math.PI / 2 })
        {
            var matches = true;
            foreach (var pipe in pipes)
            {
                var wanted = MirrorPipeDirection(pipe.OriginalPipeDirection.RotatePipeDirection(rotation), vertical);
                if (pipe.OriginalPipeDirection.RotatePipeDirection(candidate) != wanted)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return candidate.Reduced();
        }

        return mirrored;
    }

    private static PipeDirection MirrorPipeDirection(PipeDirection direction, bool vertical)
    {
        var (a, b) = vertical ? (PipeDirection.North, PipeDirection.South) : (PipeDirection.East, PipeDirection.West);
        var result = direction & ~(a | b);
        if (direction.HasFlag(a))
            result |= b;
        if (direction.HasFlag(b))
            result |= a;

        return result;
    }

    /// <summary>
    /// Mirrors a decal with its selection. Decals drawn for one side (corners, ends, lines) swap to their mirrored
    /// counterpart when one exists, since a sprite can't be flipped.
    /// </summary>
    private Decal MirrorDecal(Decal decal, Box2i extent, bool vertical)
    {
        var half = new Vector2(0.5f);
        var centre = MappingToolsMath.MirrorPoint(decal.Coordinates + half, extent, vertical);
        return new Decal(centre - half, MirrorDecalId(decal.Id, vertical), decal.Color,
            MappingToolsMath.MirrorAngle(decal.Angle, vertical), decal.ZIndex, decal.Cleanable);
    }

    private static readonly (string From, string To)[] HorizontalDecalSuffixes =
        { ("Ne", "Nw"), ("Nw", "Ne"), ("Se", "Sw"), ("Sw", "Se"), ("E", "W"), ("W", "E") };

    private static readonly (string From, string To)[] VerticalDecalSuffixes =
        { ("Ne", "Se"), ("Se", "Ne"), ("Nw", "Sw"), ("Sw", "Nw"), ("N", "S"), ("S", "N") };

    private string MirrorDecalId(string id, bool vertical)
    {
        foreach (var (from, to) in vertical ? VerticalDecalSuffixes : HorizontalDecalSuffixes)
        {
            if (!id.EndsWith(from, StringComparison.Ordinal))
                continue;

            var mirrored = id[..^from.Length] + to;
            return _prototypes.HasIndex<DecalPrototype>(mirrored) ? mirrored : id;
        }

        return id;
    }

    /// <summary>
    /// Moves a decal with its selection; its coordinates are its bottom-left, so it turns around its centre.
    /// </summary>
    private static Decal MoveDecal(Decal decal, Box2i extent, Vector2i offset, int turns)
    {
        var half = new Vector2(0.5f);
        var centre = MappingToolsMath.TransformPoint(decal.Coordinates + half, extent, offset, turns);
        return new Decal(centre - half, decal.Id, decal.Color, decal.Angle + MappingToolsMath.RotationDelta(turns),
            decal.ZIndex, decal.Cleanable);
    }
}
