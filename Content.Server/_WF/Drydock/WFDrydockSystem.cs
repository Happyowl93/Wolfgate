using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server.Cargo.Systems;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.ShipRepair;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Drydock berth console: sells repairs and refits for the ship held in its berth area.
/// </summary>
public sealed partial class WFDrydockSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    private static readonly ISawmill _sawmill = Logger.GetSawmill("drydock");
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private WFDrydockUpgradeSystem _upgrade = default!;
    [Dependency] private WFHullSectionSystem _hull = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedMaterialStorageSystem _materials = default!;

    /// <summary>Upper bound on ship tiles kept for ambient sparks.</summary>
    private const int MaxSparkTiles = 512;

    private List<Entity<MapGridComponent>> _grids = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFDrydockConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<WFDrydockConsoleComponent, WFDrydockRepairMessage>(OnRepair);
        SubscribeLocalEvent<WFDrydockConsoleComponent, WFDrydockUpgradeMessage>(OnUpgrade);
        InitializeBerthLights();
        SubscribeLocalEvent<WFDrydockCraneComponent, MapInitEvent>(OnCraneMapInit);
        SubscribeLocalEvent<WFDrydockCraneComponent, ComponentShutdown>(OnCraneShutdown);
        InitializeBooking();
        InitializeLinking();
    }

    /// <summary>The largest ship grid whose center lies in the console's berth area, or null.</summary>
    public EntityUid? GetBerthedShip(EntityUid console, WFDrydockConsoleComponent comp)
    {
        if (!TryGetBerth(console, comp, out var berth))
            return null;

        var stationXform = Transform(berth.Grid);
        var worldAabb = _transform.GetWorldMatrix(stationXform).TransformBox(berth.Area);
        var invStation = _transform.GetInvWorldMatrix(stationXform);

        _grids.Clear();
        _mapManager.FindGridsIntersecting(stationXform.MapID, worldAabb, ref _grids);

        EntityUid? best = null;
        var bestSize = 0f;
        foreach (var grid in _grids)
        {
            if (grid.Owner == berth.Grid || !HasComp<ShuttleComponent>(grid) || HasComp<MapComponent>(grid))
                continue;

            var bounds = grid.Comp.LocalAABB;
            var center = Vector2.Transform(Vector2.Transform(bounds.Center, _transform.GetWorldMatrix(grid)), invStation);
            var size = bounds.Width * bounds.Height;
            if (!berth.Area.Contains(center) || (best != null && size <= bestSize))
                continue;

            best = grid;
            bestSize = size;
        }

        return best;
    }

    /// <summary>Resolves a console's berth from its linked cranes: the area covering their reach.</summary>
    public bool TryGetBerth(EntityUid console, WFDrydockConsoleComponent comp, out WFDrydockBerth berth)
    {
        berth = default;
        if (Transform(console).GridUid is not { } grid)
            return false;

        var cranes = LinkedCranes(console);
        if (cranes.Count == 0)
            return false;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var arms = Vector2.Zero;
        foreach (var uid in cranes)
        {
            var crane = Comp<WFDrydockCraneComponent>(uid);
            var xform = Transform(uid);
            var rotation = Matrix3x2.CreateRotation((float) xform.LocalRotation.Theta);
            var toGrid = rotation * Matrix3x2.CreateTranslation(xform.LocalPosition);

            var a = crane.ArmToLocal(0f, 0f);
            var b = crane.ArmToLocal(crane.MaxExtension, crane.MaxArm);
            var corners = new[] { new Vector2(a.X, a.Y), new Vector2(b.X, a.Y), new Vector2(a.X, b.Y), new Vector2(b.X, b.Y) };
            foreach (var corner in corners)
            {
                var point = Vector2.Transform(corner, toGrid);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            arms += Vector2.Transform(new Vector2(0, -1), rotation);
        }

        // Ships come in along the arms' average direction, snapped to the nearest grid axis.
        var approach = arms.LengthSquared() < 1e-6f
            ? new Vector2(0, -1)
            : Math.Abs(arms.X) >= Math.Abs(arms.Y)
                ? new Vector2(MathF.Sign(arms.X), 0)
                : new Vector2(0, MathF.Sign(arms.Y));

        // Covers every linked crane; only working (unblocked) cranes take jobs.
        var working = cranes.Where(uid => !Comp<WFDrydockCraneComponent>(uid).Blocked).ToList();
        var label = !string.IsNullOrEmpty(comp.Berth) ? comp.Berth : MetaData(console).EntityName;
        berth = new WFDrydockBerth(grid, label, new Box2(min, max), approach, working);
        return true;
    }

    /// <summary>The cranes linked to a console that stand on its grid, in entity order.</summary>
    public List<EntityUid> LinkedCranes(EntityUid console)
    {
        var result = new List<EntityUid>();
        if (Transform(console).GridUid is not { } grid)
            return result;

        var query = EntityQueryEnumerator<WFDrydockCraneComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crane, out var xform))
        {
            if (crane.LinkedConsole == console && xform.GridUid == grid)
                result.Add(uid);
        }

        return result.OrderBy(uid => uid.Id).ToList();
    }

    /// <summary>The name a console's berth goes by in messages.</summary>
    private string BerthLabel(EntityUid console, WFDrydockConsoleComponent comp)
    {
        return TryGetBerth(console, comp, out var berth) ? berth.Label : comp.Berth;
    }

    /// <summary>Lists the work needed to restore the ship: hull tiles, missing entities, then heals.</summary>
    public List<WFDrydockWorkItem> BuildRepairItems(EntityUid ship, WFDrydockConsoleComponent comp)
    {
        var items = new List<WFDrydockWorkItem>();
        if (!TryComp<ShipRepairDataComponent>(ship, out var data) || !TryComp<MapGridComponent>(ship, out var grid))
            return items;

        AddMissingTiles(ship, grid, data, comp, items);
        AddMissingEntities(ship, data, comp, items);
        AddHeals(ship, grid, comp, items);
        return items;
    }

    /// <summary>Adds a tile item for each stored hull tile the ship no longer has.</summary>
    private void AddMissingTiles(EntityUid ship, MapGridComponent grid, ShipRepairDataComponent data, WFDrydockConsoleComponent comp, List<WFDrydockWorkItem> items)
    {
        var size = data.ChunkSize;
        foreach (var (chunkPos, chunk) in data.Chunks)
        {
            for (var i = 0; i < chunk.Tiles.Length; i++)
            {
                var stored = chunk.Tiles[i];
                if (stored == Tile.Empty.TypeId)
                    continue;

                var index = new Vector2i(chunkPos.X * size + i % size, chunkPos.Y * size + i / size);
                if (_map.GetTileRef(ship, grid, index).Tile.TypeId == stored)
                    continue;

                items.Add(new WFDrydockWorkItem
                {
                    Kind = WFDrydockWorkKind.Tile,
                    TileIndex = index,
                    TileType = stored,
                    ChunkPos = chunkPos,
                    Cost = comp.TilePrice,
                    LocalPos = new Vector2(index.X + 0.5f, index.Y + 0.5f),
                });
            }
        }
    }

    /// <summary>Adds a rebuild item for each snapshot entity the ship is missing.</summary>
    private void AddMissingEntities(EntityUid ship, ShipRepairDataComponent data, WFDrydockConsoleComponent comp, List<WFDrydockWorkItem> items)
    {
        foreach (var (chunkPos, chunk) in data.Chunks)
        {
            foreach (var (specId, spec) in chunk.Entities)
            {
                if (!IsMissing(ship, spec))
                    continue;

                var proto = _prototype.Index<EntityPrototype>(data.EntityPalette[spec.ProtoIndex].Id);
                var price = _pricing.GetEstimatedPrice(proto);
                var target = TryGetOriginal(spec, out var original) ? original : EntityUid.Invalid;

                items.Add(new WFDrydockWorkItem
                {
                    Kind = WFDrydockWorkKind.Entity,
                    ChunkPos = chunkPos,
                    SpecId = specId,
                    Target = target,
                    Cost = (int) Math.Max(comp.MinEntityPrice, price * comp.EntityPriceFactor),
                    LocalPos = spec.LocalPosition,
                });
            }
        }
    }

    /// <summary>Adds a heal item for each anchored part of the ship that has damage.</summary>
    private void AddHeals(EntityUid ship, MapGridComponent grid, WFDrydockConsoleComponent comp, List<WFDrydockWorkItem> items)
    {
        var damaged = new HashSet<Entity<DamageableComponent>>();
        _lookup.GetLocalEntitiesIntersecting(ship, grid.LocalAABB, damaged);
        foreach (var ent in damaged)
        {
            if (TerminatingOrDeleted(ent))
                continue;

            var xform = Transform(ent);
            if (xform.ParentUid != ship || !xform.Anchored || HasComp<MobStateComponent>(ent))
                continue;

            var total = ent.Comp.TotalDamage;
            if (total <= FixedPoint2.Zero)
                continue;

            items.Add(new WFDrydockWorkItem
            {
                Kind = WFDrydockWorkKind.Heal,
                Target = ent,
                Cost = (int) Math.Ceiling(total.Float() * comp.DamagePrice),
                LocalPos = xform.LocalPosition,
            });
        }
    }

    /// <summary>Applies one work item to the ship. Re-checks the item first, so repeats are harmless.</summary>
    public void ApplyItem(EntityUid ship, WFDrydockWorkItem item)
    {
        switch (item.Kind)
        {
            case WFDrydockWorkKind.Tile:
                ApplyTile(ship, item);
                break;
            case WFDrydockWorkKind.Entity:
                ApplyEntity(ship, item);
                break;
            case WFDrydockWorkKind.Heal:
                ApplyHeal(item);
                break;
            case WFDrydockWorkKind.Upgrade:
                ApplyUpgrade(item);
                break;
        }
    }

    private void ApplyTile(EntityUid ship, WFDrydockWorkItem item)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid) ||
            _map.GetTileRef(ship, grid, item.TileIndex).Tile.TypeId == item.TileType)
            return;

        _map.SetTile(ship, grid, item.TileIndex, new Tile(item.TileType));
    }

    private void ApplyEntity(EntityUid ship, WFDrydockWorkItem item)
    {
        if (!TryComp<ShipRepairDataComponent>(ship, out var data) ||
            !data.Chunks.TryGetValue(item.ChunkPos, out var chunk) ||
            !chunk.Entities.TryGetValue(item.SpecId, out var spec) ||
            !IsMissing(ship, spec))
            return;

        // Only a debris original can still be here; remove it before rebuilding.
        if (TryGetOriginal(spec, out var original))
            QueueDel(original);

        var entity = Spawn(data.EntityPalette[spec.ProtoIndex].Id, new EntityCoordinates(ship, spec.LocalPosition));
        _transform.SetLocalRotation(entity, spec.Rotation);
        spec.OriginalEntity = GetNetEntity(entity);
        Dirty(ship, data);
    }

    private void ApplyHeal(WFDrydockWorkItem item)
    {
        if (!TryComp<DamageableComponent>(item.Target, out var damageable) ||
            TerminatingOrDeleted(item.Target) ||
            damageable.TotalDamage <= FixedPoint2.Zero)
            return;

        _damageable.SetAllDamage(item.Target, damageable, FixedPoint2.Zero);
    }

    private void ApplyUpgrade(WFDrydockWorkItem item)
    {
        if (item.Upgrade == null ||
            !_prototype.TryIndex<WFDrydockUpgradePrototype>(item.Upgrade, out var proto) ||
            !Exists(item.Target) ||
            TerminatingOrDeleted(item.Target))
            return;

        if (_upgrade.GetTier(item.Target, proto) >= item.Tier)
            return;

        _upgrade.ApplyTier(item.Target, proto, item.Tier);
    }

    /// <summary>
    /// Pushes the console state to viewers, skipping unchanged states unless forced (a viewer just opened the window).
    /// </summary>
    public void UpdateUi(EntityUid console, WFDrydockConsoleComponent? comp = null, bool force = false)
    {
        if (!Resolve(console, ref comp))
            return;

        var job = CompOrNull<WFDrydockJobComponent>(console);

        // During a job, report its ship even if it drifted, and count the queue instead of scanning the grid.
        var ship = job?.Ship ?? GetBerthedShip(console, comp);
        var state = BuildState(console, ship, comp, job);
        state.PowerRatio = BerthPower(console, comp);
        state.CraneCount = LinkedCranes(console).Count;
        state.BlockedCranes = BlockedCraneCount(console);
        state.BerthLightsOn = comp.BerthLightsUntil != null;

        var key = StateKey(state);
        if (!force && key == comp.LastPushed)
            return;

        comp.LastPushed = key;
        _ui.SetUiState(console, WFDrydockConsoleUiKey.Key, state);
    }

    /// <summary>Flattens a console state into a string, so unchanged states compare equal.</summary>
    private static string StateKey(WFDrydockConsoleState s)
    {
        var parts = new List<string>
        {
            s.ShipName ?? "-",
            $"{s.MissingTiles}/{s.MissingParts}/{s.DamagedParts}/{s.RepairPrice}",
            $"{s.Working}/{s.PowerRatio}/{s.CraneCount}/{s.BlockedCranes}/{s.Deploying}/{s.Progress}/{s.RemainingSeconds}",
            $"{s.Booking}/{s.BookedBerth}/{s.BookingSeconds}/{s.Notice}/{s.PaysWithMaterials}/{s.BerthLightsOn}",
            DictKey(s.RepairMaterials),
            DictKey(s.StoredMaterials),
        };
        foreach (var u in s.Upgrades)
        {
            parts.Add($"{u.Id}:{u.Name}:{u.Parts}:{u.MinTier}:{u.MaxTier}:{u.NextPrice}:{DictKey(u.NextMaterials)}");
        }

        return string.Join("|", parts);
    }

    private static string DictKey(Dictionary<string, int> bill)
    {
        return string.Join(",", bill.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
    }

    /// <summary>Lowest power ratio among the berth's cranes, or 1 without any.</summary>
    private float BerthPower(EntityUid console, WFDrydockConsoleComponent comp)
    {
        if (!TryGetBerth(console, comp, out var berth) || berth.Cranes.Count == 0)
            return 1f;

        return berth.Cranes.Min(c => CranePower(c));
    }

    /// <summary>Whether every berth crane has at least half its power, so work can start.</summary>
    private bool CranesPowered(EntityUid console, WFDrydockConsoleComponent comp)
    {
        return !TryGetBerth(console, comp, out var berth) || berth.Cranes.All(c => CranePower(c) >= 0.5f);
    }

    /// <summary>Builds the console state for a ship in or booked to a berth: its quote, job progress and refits.</summary>
    private WFDrydockConsoleState BuildState(EntityUid console, EntityUid? ship, WFDrydockConsoleComponent comp, WFDrydockJobComponent? job)
    {
        var state = new WFDrydockConsoleState();
        var pay = CompOrNull<WFDrydockMaterialPaymentComponent>(console);
        if (pay != null)
        {
            state.PaysWithMaterials = true;
            // Same stock the job's charge draws from: the cranes' storage and their linked silos.
            foreach (var (id, amount) in Availability(WorkingCranes(console, comp)))
            {
                state.StoredMaterials[id] = amount;
            }
        }

        if (ship is { } berthed)
        {
            state.ShipName = MetaData(berthed).EntityName;

            var items = job != null
                ? job.Pending.Concat(job.Slots.Where(s => s.Item != null).Select(s => s.Item!)).ToList()
                : BuildRepairItems(berthed, comp);
            CountWork(state, items);

            if (pay != null && job == null && items.Count > 0)
            {
                AssignMaterials(pay, items);
                state.RepairMaterials = SumBills(items);
            }
            else if (job == null && items.Count > 0)
                state.RepairPrice = WorkPrice(comp, null, items);
        }

        if (job != null)
        {
            state.Working = true;
            state.Deploying = _timing.CurTime < job.WorkStarts;
            state.Progress = job.Total == 0 ? 0f : (float) job.Done / job.Total;
            state.RemainingSeconds = (float) Math.Max(0d, (job.EstimatedEnd - _timing.CurTime).TotalSeconds);
        }

        foreach (var id in comp.Upgrades)
        {
            if (_prototype.TryIndex<WFDrydockUpgradePrototype>(id.Id, out var proto))
                state.Upgrades.Add(BuildUpgradeEntry(proto, ship, pay, scanParts: job == null));
        }

        return state;
    }

    /// <summary>Tallies work items into the state's missing tile, missing part and damaged part counts.</summary>
    private static void CountWork(WFDrydockConsoleState state, List<WFDrydockWorkItem> items)
    {
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case WFDrydockWorkKind.Tile:
                    state.MissingTiles++;
                    break;
                case WFDrydockWorkKind.Entity:
                    state.MissingParts++;
                    break;
                case WFDrydockWorkKind.Heal:
                    state.DamagedParts++;
                    break;
            }
        }
    }

    /// <summary>One refit line for the console: the ship's part count and the price of its next tier.</summary>
    private WFDrydockUpgradeEntry BuildUpgradeEntry(WFDrydockUpgradePrototype proto, EntityUid? ship, WFDrydockMaterialPaymentComponent? pay, bool scanParts)
    {
        var entry = new WFDrydockUpgradeEntry
        {
            Id = proto.ID,
            Name = Loc.GetString(proto.Name),
            MaxTier = proto.Tiers.Count,
        };

        // Skipped during a job, when the upgrade buttons are disabled.
        if (ship is not { } shipUid || !scanParts)
            return entry;

        var parts = new List<EntityUid>();
        _upgrade.GetParts(shipUid, proto, parts);
        entry.Parts = parts.Count;
        if (parts.Count == 0)
            return entry;

        var minTier = parts.Min(p => _upgrade.GetTier(p, proto));
        entry.MinTier = minTier;
        if (minTier < proto.Tiers.Count)
            entry.NextPrice = parts.Count(p => _upgrade.GetTier(p, proto) <= minTier) * proto.Tiers[minTier].Price;

        if (pay != null && minTier < proto.Tiers.Count)
        {
            var tier = proto.Tiers[minTier];
            var perPart = ItemBill(pay, tier.Price, tier.Materials);
            var ready = parts.Count(p => _upgrade.GetTier(p, proto) <= minTier);
            for (var i = 0; i < ready; i++)
            {
                AddBill(entry.NextMaterials, perPart);
            }
        }

        return entry;
    }

    private void OnUiOpened(EntityUid uid, WFDrydockConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, comp, force: true);
    }

    private void OnRepair(EntityUid uid, WFDrydockConsoleComponent comp, WFDrydockRepairMessage args)
    {
        if (TryGetWorkableShip(uid, comp, args.Actor, out var berthed))
            TryStartRepair(uid, comp, berthed, args.Actor);
    }

    private void OnUpgrade(EntityUid uid, WFDrydockConsoleComponent comp, WFDrydockUpgradeMessage args)
    {
        var actor = args.Actor;
        if (!comp.Upgrades.Any(u => u.Id == args.Upgrade) || !_prototype.TryIndex<WFDrydockUpgradePrototype>(args.Upgrade, out _))
        {
            Deny(uid, comp, actor, Loc.GetString("drydock-console-unknown-refit"));
            return;
        }

        if (TryGetWorkableShip(uid, comp, actor, out var berthed))
            TryStartWork(uid, comp, berthed, actor, args.Upgrade);
    }

    /// <summary>Starts a repair on the ship in this berth, paid by the actor.</summary>
    public bool TryStartRepair(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship, EntityUid actor)
    {
        return TryStartWork(console, comp, ship, actor, null);
    }

    /// <summary>Validates and charges a repair (null upgrade) or refit, then starts its job on the ship.</summary>
    private bool TryStartWork(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship, EntityUid actor, string? upgrade, bool booked = false)
    {
        if (BuildWork(ship, comp, upgrade, out var items) is { } denial)
            return Deny(console, comp, actor, denial);

        RefreshCranes(console);
        if (LinkedCranes(console).Count > 0 && (!TryGetBerth(console, comp, out var working) || working.Cranes.Count == 0))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-cranes-blocked"));

        if (!CranesPowered(console, comp))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-power"));

        if (TryGetBerth(console, comp, out var berth) && berth.Cranes.Any(c => CraneBusyElsewhere(console, c)))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-crane-busy"));

        // Booked jobs are always paid in credits; walk-in material consoles pay from their storage.
        var pay = booked ? null : CompOrNull<WFDrydockMaterialPaymentComponent>(console);
        if (pay != null)
        {
            AssignMaterials(pay, items);
            if (!TryPayMaterials(console, comp, actor, items))
                return false;

            StartJob(console, comp, ship, null, items, 0, paidInMaterials: true);
            _popup.PopupCursor(Loc.GetString("drydock-console-started-materials", ("count", items.Count)), actor);
            return true;
        }

        var price = WorkPrice(comp, upgrade, items);
        if (!TryCharge(console, comp, actor, price))
            return false;

        StartJob(console, comp, ship, actor, items, price);
        return true;
    }

    /// <summary>
    /// Finds the berthed ship and checks the drydock can start on it: idle, unreserved for others, and held still.
    /// Clears the console's booking on success.
    /// </summary>
    private bool TryGetWorkableShip(EntityUid console, WFDrydockConsoleComponent comp, EntityUid actor, out EntityUid ship)
    {
        ship = EntityUid.Invalid;
        if (GetBerthedShip(console, comp) is not { } berthed)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-ship"));

        if (HasComp<WFDrydockJobComponent>(console))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-busy"));

        if (TryComp<WFDrydockBookingComponent>(console, out var reserved) && reserved.Ship != berthed)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-reserved", ("berth", BerthLabel(console, comp)), ("ship", Name(reserved.Ship))));

        if (!IsHeldStill(console, comp, berthed))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-ship-moving"));

        if (IsShipyardGrid(console, comp, berthed))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-own-grid"));

        if (TryComp<WFDrydockBookingComponent>(console, out var booking))
            RemoveBooking(console, booking);

        ship = berthed;
        return true;
    }

    /// <summary>Whether a ship moves with the drydock's station no faster than the console's start speed.</summary>
    private bool IsHeldStill(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship)
    {
        var stationVelocity = Transform(console).GridUid is { } station ? _physics.GetMapLinearVelocity(station) : Vector2.Zero;
        return (_physics.GetMapLinearVelocity(ship) - stationVelocity).Length() <= comp.MaxStartSpeed;
    }

    /// <summary>Withdraws the price from the actor's bank. Shows the reason and plays the deny sound on failure.</summary>
    private bool TryCharge(EntityUid console, WFDrydockConsoleComponent comp, EntityUid actor, int price)
    {
        if (!TryComp<BankAccountComponent>(actor, out var bank))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-bank-account"));

        if (!HasCharacter(actor))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-character"));

        if (bank.Balance < price)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-insufficient-funds", ("price", price.ToString("N0"))));

        // The entity overload also updates the actor's cached balance, which the bank UI reads.
        if (!_bank.TryBankWithdraw(actor, price))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-transaction-failed"));

        return true;
    }

    /// <summary>Whether the actor is a connected player with a selected humanoid character.</summary>
    private bool HasCharacter(EntityUid actor)
    {
        return TryComp<ActorComponent>(actor, out var actorComp) &&
               _prefs.TryGetCachedPreferences(actorComp.PlayerSession.UserId, out var prefs) &&
               _players.TryGetSessionByEntity(actor, out _) &&
               prefs.SelectedCharacter is HumanoidCharacterProfile;
    }

    /// <summary>True when the ship is the console's grid or the grid of one of its berth cranes.</summary>
    private bool IsShipyardGrid(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship)
    {
        if (Transform(console).GridUid == ship)
            return true;

        return TryGetBerth(console, comp, out var berth) && berth.Cranes.Any(c => Transform(c).GridUid == ship);
    }

    /// <summary>Starts a paid job: queues its items, then deploys the cranes over the ship.</summary>
    public void StartJob(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship, EntityUid? payer, List<WFDrydockWorkItem> items, int price, bool paidInMaterials = false)
    {
        if (IsShipyardGrid(console, comp, ship))
        {
            _sawmill.Warning($"Refused a drydock job on its own grid {ToPrettyString(ship)} from {ToPrettyString(console)}.");
            return;
        }

        TurnOffBerthLights(console, comp);
        var job = AddComp<WFDrydockJobComponent>(console);
        job.Ship = ship;
        job.Payer = payer;
        job.PaidInMaterials = paidInMaterials;
        job.Total = items.Count;
        job.Done = 0;

        AddSlots(console, comp, job);
        QueueItems(comp, job, items);
        CollectSparkTiles(ship, job);

        // Boom paths are cached before the cranes swing out, so their first targets use them.
        ScanJobBooms(job);

        // The cranes swing out over the ship and extend to its size before any work begins.
        var curTime = _timing.CurTime;
        job.WorkStarts = curTime + TimeSpan.FromSeconds(DeployCranes(comp, job));
        job.NextSpark = job.WorkStarts;
        job.NextIndustry = curTime;
        job.NextBerthCheck = curTime;
        UpdateEstimate(job, curTime);

        if (payer is { } payerUid)
        {
            var message = items[0].Kind == WFDrydockWorkKind.Upgrade
                ? Loc.GetString("drydock-console-refit-started", ("count", items.Count))
                : Loc.GetString("drydock-console-started", ("count", items.Count), ("price", price.ToString("N0")));
            _popup.PopupCursor(message, payerUid);
        }

        _audio.PlayPvs(comp.ConfirmSound, console);
        UpdateUi(console, comp);
    }

    /// <summary>One slot per berth crane; a berth without cranes still gets one slot.</summary>
    private void AddSlots(EntityUid console, WFDrydockConsoleComponent comp, WFDrydockJobComponent job)
    {
        if (TryGetBerth(console, comp, out var berth))
        {
            foreach (var crane in berth.Cranes)
                job.Slots.Add(new WFDrydockJobSlot { Crane = crane });
        }

        if (job.Slots.Count == 0)
            job.Slots.Add(new WFDrydockJobSlot());
    }

    /// <summary>Sets each item's crane work time, scales the job to fit MaxDuration, and queues it.</summary>
    private static void QueueItems(WFDrydockConsoleComponent comp, WFDrydockJobComponent job, List<WFDrydockWorkItem> items)
    {
        var workTotal = 0f;
        foreach (var item in items)
        {
            item.WorkTime = item.Kind switch
            {
                WFDrydockWorkKind.Tile => comp.TileWorkTime,
                WFDrydockWorkKind.Entity => comp.EntityWorkTime,
                WFDrydockWorkKind.Heal => comp.HealWorkTime,
                _ => comp.UpgradeWorkTime,
            };
            workTotal += item.WorkTime;
        }

        var span = workTotal / job.Slots.Count;
        var scale = span > comp.MaxDuration ? comp.MaxDuration / span : 1f;
        foreach (var item in items)
        {
            item.WorkTime *= scale;
            job.Pending.Add(item);
        }
    }

    /// <summary>Records the centers of the ship's tiles for ambient sparks, up to MaxSparkTiles.</summary>
    private void CollectSparkTiles(EntityUid ship, WFDrydockJobComponent job)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return;

        var tiles = _map.GetAllTilesEnumerator(ship, grid);
        while (job.SparkTiles.Count < MaxSparkTiles && tiles.MoveNext(out var tile))
        {
            var indices = tile!.Value.GridIndices;
            job.SparkTiles.Add(new Vector2(indices.X + 0.5f, indices.Y + 0.5f));
        }
    }

    /// <summary>Shows a refusal to the actor, plays the deny sound and returns false.</summary>
    private bool Deny(EntityUid console, WFDrydockConsoleComponent comp, EntityUid actor, string message)
    {
        // At the cursor, so a caller working the console from their helm sees it too.
        _popup.PopupCursor(message, actor);
        _audio.PlayPvs(comp.DenySound, console);
        return false;
    }

    /// <summary>Whether a snapshot entity is gone, or its original sits on debris of the ship.</summary>
    private bool IsMissing(EntityUid ship, ShipRepairEntitySpecifier spec)
    {
        if (!TryGetOriginal(spec, out var original))
            return true;

        var grid = Transform(original).GridUid;
        return grid is { } onGrid && onGrid != ship && _hull.IsDebrisOf(onGrid, ship);
    }

    /// <summary>Resolves a spec's recorded original entity, or false if it no longer exists.</summary>
    private bool TryGetOriginal(ShipRepairEntitySpecifier spec, out EntityUid original)
    {
        original = EntityUid.Invalid;
        if (spec.OriginalEntity is not { } net ||
            !TryGetEntity(net, out var resolved) ||
            resolved is not { } uid ||
            TerminatingOrDeleted(uid))
            return false;

        original = uid;
        return true;
    }
}
