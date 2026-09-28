using System.Numerics;
using Robust.Shared.Utility;
using Content.Server.Decals;
using Content.Shared._WF.MappingTools;
using Content.Shared.Administration;
using Content.Shared.Decals;
using Content.Shared.GameTicking;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Placement;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.MappingTools;

public sealed partial class MappingToolsSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedPlayerManager _players = default!;

    /// <summary>
    /// Edits kept per mapper; the oldest are dropped past this.
    /// </summary>
    private const int MaxHistory = 100;

    private sealed class History
    {
        public readonly List<MappingEdit> Undo = new();
        public readonly List<MappingEdit> Redo = new();
    }

    private readonly Dictionary<ICommonSession, History> _histories = new();

    /// <summary>
    /// The last tile change seen, claimed by the <see cref="PlacementTileEvent"/> the engine raises right after it.
    /// </summary>
    private MappingTileChange? _lastTileChange;

    private readonly List<PendingDecal> _pendingDecals = new();

    /// <summary>
    /// A decal placement waiting for the decal system to add it, with the decals that were already there.
    /// </summary>
    private sealed record PendingDecal(ICommonSession Session, EntityUid Grid, Vector2 Position, string Id, HashSet<uint> Before);

    private void InitializeHistory()
    {
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<PlacementTileEvent>(OnPlacementTile);
        SubscribeLocalEvent<PlacementEntityEvent>(OnPlacementEntity);
        SubscribeNetworkEvent<RequestDecalPlacementEvent>(OnDecalPlacement, before: [typeof(DecalSystem)]);
        SubscribeNetworkEvent<RequestDecalRemovalEvent>(OnDecalRemoval, before: [typeof(DecalSystem)]);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _lastTileChange = null;

        foreach (var pending in _pendingDecals)
        {
            if (TerminatingOrDeleted(pending.Grid))
                continue;

            var edit = new MappingEdit("wf-mapping-tools-edit-decal");
            foreach (var (id, decal) in _decals.GetDecalsInRange(pending.Grid, pending.Position, 1.5f))
            {
                if (!pending.Before.Contains(id) && decal.Id == pending.Id)
                    edit.DecalsAdded.Add(new MappingDecal(pending.Grid, decal));
            }

            RecordPlacement(pending.Session, edit);
        }

        _pendingDecals.Clear();
    }

    /// <summary>
    /// Applies a new edit and pushes it onto the mapper's history.
    /// </summary>
    private void Commit(ICommonSession session, MappingEdit edit)
    {
        if (edit.IsEmpty)
            return;

        Apply(edit, forward: true);
        Push(session, edit);
    }

    private void Push(ICommonSession session, MappingEdit edit)
    {
        var history = _histories.GetOrNew(session);
        history.Redo.Clear();
        history.Undo.Add(edit);

        if (history.Undo.Count > MaxHistory)
            history.Undo.RemoveAt(0);
    }

    /// <summary>
    /// Records an edit the placement menu already made, merged with others from the same tick.
    /// </summary>
    private void RecordPlacement(ICommonSession session, MappingEdit edit)
    {
        if (edit.IsEmpty || !CanUse(session))
            return;

        var tick = _timing.CurTick;
        if (_histories.TryGetValue(session, out var history) &&
            history.Redo.Count == 0 &&
            history.Undo.Count > 0 &&
            history.Undo[^1] is { PlacementTick: { } last } previous &&
            last == tick)
        {
            previous.Merge(edit);
            return;
        }

        edit.PlacementTick = tick;
        Push(session, edit);
    }

    /// <summary>
    /// Undoes the mapper's last edit, or redoes their last undone one.
    /// </summary>
    public void StepHistory(ICommonSession session, bool redo)
    {
        var history = _histories.GetOrNew(session);
        var from = redo ? history.Redo : history.Undo;
        var to = redo ? history.Undo : history.Redo;

        if (from.Count == 0)
        {
            _popup.PopupCursor(Loc.GetString(redo ? "wf-mapping-tools-nothing-to-redo" : "wf-mapping-tools-nothing-to-undo"), session);
            return;
        }

        var edit = from[^1];
        from.RemoveAt(from.Count - 1);
        Apply(edit, forward: redo);
        to.Add(edit);

        _popup.PopupCursor(Loc.GetString(redo ? "wf-mapping-tools-redid" : "wf-mapping-tools-undid",
            ("edit", Loc.GetString(edit.Name))), session);
    }

    private void OnTileChanged(ref TileChangedEvent ev)
    {
        if (_applying || ev.Changes.Length == 0)
            return;

        var change = ev.Changes[^1];
        _lastTileChange = new MappingTileChange(ev.Entity, change.GridIndices, change.OldTile, change.NewTile);
    }

    private void OnPlacementTile(PlacementTileEvent ev)
    {
        if (_lastTileChange is not { } change || change.New.TypeId != ev.TileType)
            return;

        _lastTileChange = null;
        if (!_players.TryGetSessionById(ev.PlacerNetUserId, out var session))
            return;

        var edit = new MappingEdit("wf-mapping-tools-edit-tile");
        edit.Tiles.Add(change);
        RecordPlacement(session, edit);
    }

    private void OnPlacementEntity(PlacementEntityEvent ev)
    {
        if (!_players.TryGetSessionById(ev.PlacerNetUserId, out var session) || !CanUse(session))
            return;

        var uid = ev.EditedEntity;
        var parent = Transform(uid).ParentUid;
        if (!IsSelectable(uid) || !HasComp<MapGridComponent>(parent) && !HasComp<MapComponent>(parent))
            return;

        var group = new MappingEntityGroup();
        group.Roots.Add(new MappingGroupRoot(GetRef(uid), -1, PoseOf(uid)));

        MappingEdit edit;
        if (ev.PlacementEventAction == PlacementEventAction.Create)
        {
            edit = new MappingEdit("wf-mapping-tools-edit-place");
            edit.Created.Add(group);
        }
        else
        {
            // Raised before the engine deletes it; save it now so undo can bring it back.
            edit = new MappingEdit("wf-mapping-tools-edit-erase");
            SaveGroup(group, [uid]);
            edit.Deleted.Add(group);
        }

        RecordPlacement(session, edit);
    }

    private void OnDecalPlacement(RequestDecalPlacementEvent ev, EntitySessionEventArgs args)
    {
        if (!CanUse(args.SenderSession))
            return;

        var coords = GetCoordinates(ev.Coordinates);
        if (!coords.IsValid(EntityManager) || _transform.GetGrid(coords) is not { } grid || !HasComp<MapGridComponent>(grid))
            return;

        var position = _transform.WithEntityId(coords, grid).Position;
        var before = new HashSet<uint>();
        foreach (var (id, _) in _decals.GetDecalsInRange(grid, position, 1.5f))
        {
            before.Add(id);
        }

        _pendingDecals.Add(new PendingDecal(args.SenderSession, grid, position, ev.Decal.Id, before));
    }

    private void OnDecalRemoval(RequestDecalRemovalEvent ev, EntitySessionEventArgs args)
    {
        // The decal system only removes for Spawn admins.
        if (!CanUse(args.SenderSession) || !_admin.HasAdminFlag(args.SenderSession, AdminFlags.Spawn))
            return;

        var coords = GetCoordinates(ev.Coordinates);
        if (!coords.IsValid(EntityManager) || _transform.GetGrid(coords) is not { } grid)
            return;

        // Mirrors the decal system, which removes every decal in range of the raw position.
        var edit = new MappingEdit("wf-mapping-tools-edit-decal");
        foreach (var (_, decal) in _decals.GetDecalsInRange(grid, ev.Coordinates.Position))
        {
            edit.DecalsRemoved.Add(new MappingDecal(grid, decal));
        }

        RecordPlacement(args.SenderSession, edit);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        _histories.Remove(args.Session);
        _clipboards.Remove(args.Session);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _histories.Clear();
        _clipboards.Clear();
        _refs.Clear();
        _lastTileChange = null;
        _pendingDecals.Clear();
    }
}
