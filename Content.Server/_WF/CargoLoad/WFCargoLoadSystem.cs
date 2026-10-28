using System.Linq;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Planets.Flight;
using Content.Server.Popups;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.CargoLoad;
using Content.Shared.Examine;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared._Mono.Ships;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.CargoLoad;

/// <summary>
/// Cargo weight. Each grid sums the weight of the cargo aboard once a second; a loaded docked set accelerates more slowly
/// in space, cargo counts against planet lift through <see cref="WFGridLiftLoadEvent"/>, and a heavy hold spools FTL
/// longer. Anchors (grids that cannot fly) bound a set, so ships at one station never share a penalty.
/// </summary>
public sealed class WFCargoLoadSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IComponentFactory _factory = default!;

    public static readonly ProtoId<WFCargoLoadPrototype> DefaultProfile = "WFCargoLoadDefault";
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextSweep;
    private readonly Dictionary<EntityUid, float> _loads = new();
    private readonly Dictionary<EntityUid, float> _stored = new();
    private readonly HashSet<EntityUid> _touched = new();

    /// <summary>Loose tonnes per unit of each material, from its sheet or ore.</summary>
    private readonly Dictionary<string, float> _materialWeights = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCargoLoadComponent, WFGridLiftLoadEvent>(OnLiftLoad);
        SubscribeLocalEvent<WFCargoMassComponent, ExaminedEvent>(OnCargoExamined);
        SubscribeLocalEvent<WFCargoHelmComponent, ExaminedEvent>(OnHelmExamined);
        SubscribeLocalEvent<MaterialStorageComponent, ExaminedEvent>(OnStorageExamined);
        SubscribeLocalEvent<WFLiftWarningEvent>(OnLiftWarning);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _materialWeights.Clear());
    }

    /// <summary>Names cargo in the descent warning when dropping some would restore full lift.</summary>
    private void OnLiftWarning(ref WFLiftWarningEvent args)
    {
        if (HasComp<WFEncounterGridComponent>(args.Grid))
            return;

        var set = GetCargoSet(args.Grid);
        var load = GetSetLoad(set);
        var hull = set.Sum(member => CompOrNull<PhysicsComponent>(member)?.FixturesMass ?? 0f);
        if (load <= 0f || hull <= 0f || args.Ratio * (hull + load) / hull < args.FullRatio)
            return;

        var drop = (hull + load) * (1f - args.Ratio / args.FullRatio);
        var heavy = Profile.Classes.GetValueOrDefault("Heavy", 3f) * Profile.MassScale;
        args.Hint = Loc.GetString("wf-cargo-load-lift-hint",
            ("tonnes", (int) MathF.Ceiling(drop)), ("crates", (int) MathF.Ceiling(drop / Math.Max(0.1f, heavy))));
    }

    private WFCargoLoadPrototype Profile => _proto.Index(DefaultProfile);

    /// <summary>Tonnes one piece of cargo weighs.</summary>
    public float Weight(EntityUid uid, WFCargoMassComponent? cargo = null)
    {
        if (!Resolve(uid, ref cargo, false))
            return 0f;

        var profile = Profile;
        if (cargo.PerUnit > 0f)
            return cargo.PerUnit * (CompOrNull<StackComponent>(uid)?.Count ?? 1) * profile.MassScale;

        var cls = cargo.Class;
        if (cls == null && MetaData(uid).EntityPrototype is { } proto && profile.Goods.TryGetValue(proto.ID, out var listed))
            cls = listed;

        cls ??= profile.DefaultClass;
        return profile.Classes.GetValueOrDefault(cls) * profile.MassScale;
    }

    /// <summary>Loose tonnes one unit of a material weighs: its sheet or ore's weight over its units; 0 if it has none.</summary>
    public float MaterialWeight(string material)
    {
        if (_materialWeights.TryGetValue(material, out var cached))
            return cached;

        var weight = 0f;
        if (_proto.TryIndex<MaterialPrototype>(material, out var proto)
            && proto.StackEntity is { } stack
            && _proto.TryIndex(stack, out var item)
            && item.TryGetComponent<WFCargoMassComponent>(out var cargo, _factory)
            && cargo.PerUnit > 0f
            && item.TryGetComponent<PhysicalCompositionComponent>(out var composition, _factory)
            && composition.MaterialComposition.TryGetValue(material, out var units)
            && units > 0)
            weight = cargo.PerUnit / units;

        _materialWeights[material] = weight;
        return weight;
    }

    /// <summary>Tonnes the materials in a storage weigh, compacted by <see cref="WFCargoLoadPrototype.StoredFactor"/>.</summary>
    public float StoredWeight(MaterialStorageComponent storage)
    {
        var loose = 0f;
        foreach (var (material, amount) in storage.Storage)
        {
            loose += amount * MaterialWeight(material);
        }

        return loose * Profile.StoredFactor * Profile.MassScale;
    }

    /// <summary>Tonnes of a set's load held as stored materials.</summary>
    public float GetSetStored(IEnumerable<EntityUid> grids)
    {
        return grids.Sum(grid => CompOrNull<WFCargoLoadComponent>(grid)?.OwnStored ?? 0f);
    }

    /// <summary>Tonnes one crate of a good weighs, from the class table.</summary>
    public float GoodWeight(string good)
    {
        var profile = Profile;
        var cls = profile.Goods.TryGetValue(good, out var listed) ? listed : profile.DefaultClass;
        return profile.Classes.GetValueOrDefault(cls) * profile.MassScale;
    }

    /// <summary>The weight class a piece of cargo is in.</summary>
    public string ClassOf(EntityUid uid, WFCargoMassComponent cargo)
    {
        if (cargo.Class != null)
            return cargo.Class;

        return MetaData(uid).EntityPrototype is { } proto && Profile.Goods.TryGetValue(proto.ID, out var listed)
            ? listed
            : Profile.DefaultClass;
    }

    /// <summary>Tonnes of cargo aboard one grid.</summary>
    public float GetLoad(EntityUid grid)
    {
        return CompOrNull<WFCargoLoadComponent>(grid)?.OwnLoad ?? 0f;
    }

    /// <summary>Tonnes of cargo aboard a set of grids.</summary>
    public float GetSetLoad(IEnumerable<EntityUid> grids)
    {
        return grids.Sum(GetLoad);
    }

    /// <summary>Share of thrust a grid's set keeps in space; 1 with no cargo.</summary>
    public float GetSpaceFactor(EntityUid grid)
    {
        return CompOrNull<WFCargoLoadComponent>(grid)?.SpaceFactor ?? 1f;
    }

    /// <summary>A grid that cannot fly: force-anchored or a static body. Anchors bound cargo sets.</summary>
    public bool IsAnchor(EntityUid grid)
    {
        return HasComp<ForceAnchorComponent>(grid)
               || TryComp<PhysicsComponent>(grid, out var body) && body.BodyType == BodyType.Static;
    }

    /// <summary>
    /// The grids pushed together with one: everything docked to it, through any dock whatever its FTL lock, stopping at
    /// anchors and never taking in a map. An anchor's set is itself.
    /// </summary>
    public HashSet<EntityUid> GetCargoSet(EntityUid grid)
    {
        var set = new HashSet<EntityUid> { grid };
        if (IsAnchor(grid))
            return set;

        var queue = new Queue<EntityUid>();
        queue.Enqueue(grid);
        while (queue.TryDequeue(out var current))
        {
            foreach (var dock in _docking.GetDocks(current))
            {
                if (!dock.Comp.Docked || dock.Comp.DockedWith is not { } other
                    || Transform(other).GridUid is not { } otherGrid
                    || set.Contains(otherGrid)
                    || HasComp<MapComponent>(otherGrid)
                    || IsAnchor(otherGrid))
                    continue;

                set.Add(otherGrid);
                queue.Enqueue(otherGrid);
            }
        }

        return set;
    }

    /// <summary>Seconds of FTL spool cargo adds to a jump; 0 for drives mass doesn't affect.</summary>
    public float SpoolPenalty(EntityUid grid, FTLDriveComponent drive)
    {
        if (!drive.MassAffectedDrive)
            return 0f;

        var jumping = new HashSet<EntityUid>();
        _shuttle.GetAllDockedShuttles(grid, jumping);
        jumping.Add(grid);
        var profile = Profile;
        return Math.Min(profile.SpoolCap, profile.SpoolPerTonne * GetSetLoad(jumping));
    }

    private void OnLiftLoad(Entity<WFCargoLoadComponent> ent, ref WFGridLiftLoadEvent args)
    {
        // NPC crews can't dump cargo yet, so their hulls don't count it against planet lift.
        if (!HasComp<WFEncounterGridComponent>(ent))
            args.Mass += ent.Comp.OwnLoad;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;
        Sweep();
    }

    private void Sweep()
    {
        _loads.Clear();
        var cargo = EntityQueryEnumerator<WFCargoMassComponent, TransformComponent>();
        while (cargo.MoveNext(out var uid, out var mass, out var xform))
        {
            if (xform.GridUid is not { } grid || HasComp<MapComponent>(grid))
                continue;

            _loads[grid] = _loads.GetValueOrDefault(grid) + Weight(uid, mass);
        }

        // Sheets and ore put into storage become numbers, so storage weighs its contents itself.
        _stored.Clear();
        var storages = EntityQueryEnumerator<MaterialStorageComponent, TransformComponent>();
        while (storages.MoveNext(out var storage, out var xform))
        {
            if (xform.GridUid is not { } grid || HasComp<MapComponent>(grid) || storage.Storage.Count == 0)
                continue;

            var weight = StoredWeight(storage);
            if (weight <= 0f)
                continue;

            _stored[grid] = _stored.GetValueOrDefault(grid) + weight;
            _loads[grid] = _loads.GetValueOrDefault(grid) + weight;
        }

        _touched.Clear();
        var profile = Profile;
        foreach (var grid in _loads.Keys.ToList())
        {
            if (_touched.Contains(grid))
                continue;

            var set = GetCargoSet(grid);
            var load = set.Sum(member => _loads.GetValueOrDefault(member));
            var hull = set.Sum(member => CompOrNull<PhysicsComponent>(member)?.FixturesMass ?? 0f);
            var factor = load <= 0f ? 1f : Math.Max(profile.ThrustFloor, hull / Math.Max(0.001f, hull + load));
            var stored = set.Sum(member => _stored.GetValueOrDefault(member));
            foreach (var member in set)
            {
                if (!_touched.Add(member))
                    continue;

                var comp = EnsureComp<WFCargoLoadComponent>(member);
                comp.OwnLoad = _loads.GetValueOrDefault(member);
                comp.OwnStored = _stored.GetValueOrDefault(member);
                comp.SetLoad = load;
                comp.SpaceFactor = factor;
                comp.SetStored = stored;
                comp.SetHull = hull;
            }
        }

        var stale = new List<EntityUid>();
        var loaded = EntityQueryEnumerator<WFCargoLoadComponent>();
        while (loaded.MoveNext(out var uid, out var comp))
        {
            if (!_touched.Contains(uid) || comp.SetLoad <= 0f)
                stale.Add(uid);
        }

        foreach (var uid in stale)
        {
            RemComp<WFCargoLoadComponent>(uid);
        }

        UpdateReadouts(profile);
        UpdateHelms(profile);
    }

    /// <summary>Publishes each loaded ship's cargo to its helm screens, resending only when a shown figure changes.</summary>
    private void UpdateReadouts(WFCargoLoadPrototype profile)
    {
        var query = EntityQueryEnumerator<WFCargoLoadComponent>();
        while (query.MoveNext(out var uid, out var cargo))
        {
            if (IsAnchor(uid))
                continue;

            var spool = _shuttle.TryGetFTLDrive(uid, out _, out var drive) ? SpoolPenalty(uid, drive) : 0f;
            var fullScale = cargo.SetHull * (1f / Math.Max(0.01f, profile.ThrustFloor) - 1f);
            var readout = EnsureComp<WFCargoReadoutComponent>(uid);
            if (Near(readout.Load, cargo.SetLoad, 0.1f)
                && Near(readout.Stored, cargo.SetStored, 0.1f)
                && Near(readout.Thrust, cargo.SpaceFactor, 0.005f)
                && Near(readout.FullScale, fullScale, 1f)
                && Near(readout.Spool, spool, 0.25f))
                continue;

            readout.Load = cargo.SetLoad;
            readout.Stored = cargo.SetStored;
            readout.Thrust = cargo.SpaceFactor;
            readout.FullScale = fullScale;
            readout.Spool = spool;
            Dirty(uid, readout);
        }

        var stale = new List<EntityUid>();
        var readouts = EntityQueryEnumerator<WFCargoReadoutComponent>();
        while (readouts.MoveNext(out var uid, out _))
        {
            if (!HasComp<WFCargoLoadComponent>(uid) || IsAnchor(uid))
                stale.Add(uid);
        }

        foreach (var uid in stale)
        {
            RemComp<WFCargoReadoutComponent>(uid);
        }
    }

    private static bool Near(float a, float b, float step)
    {
        return MathF.Abs(a - b) < step;
    }

    /// <summary>Gives loaded ships' helms their readout and warns their pilots as the load crosses a band.</summary>
    private void UpdateHelms(WFCargoLoadPrototype profile)
    {
        var helms = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (helms.MoveNext(out var uid, out var console, out var xform))
        {
            var loaded = xform.GridUid is { } grid
                         && TryComp<WFCargoLoadComponent>(grid, out var load)
                         && load.SetLoad > 0f
                         && !IsAnchor(grid);

            if (!loaded)
            {
                RemComp<WFCargoHelmComponent>(uid);
                continue;
            }

            var cargo = Comp<WFCargoLoadComponent>(xform.GridUid!.Value);
            var helm = EnsureComp<WFCargoHelmComponent>(uid);
            var percent = (int) MathF.Round((1f - cargo.SpaceFactor) * 100f);
            for (var i = 0; i < profile.Bands.Count; i++)
            {
                var band = profile.Bands[i];
                if (cargo.SpaceFactor < band && helm.Told.Add(i))
                {
                    Warn(console, Loc.GetString("wf-cargo-load-band", ("tonnes", (int) MathF.Round(cargo.SetLoad)), ("percent", percent)));
                }
                else if (cargo.SpaceFactor >= band + profile.Hysteresis && helm.Told.Remove(i))
                {
                    Warn(console, Loc.GetString("wf-cargo-load-restored", ("percent", percent)));
                }
            }

            if (cargo.SpaceFactor < profile.Bands.FirstOrDefault(1f))
            {
                foreach (var pilot in console.SubscribedPilots)
                {
                    if (helm.Pilots.Add(pilot))
                        _popup.PopupEntity(Loc.GetString("wf-cargo-load-band", ("tonnes", (int) MathF.Round(cargo.SetLoad)), ("percent", percent)), pilot, pilot);
                }
            }

            helm.Pilots.RemoveWhere(pilot => !console.SubscribedPilots.Contains(pilot));
        }
    }

    private void Warn(ShuttleConsoleComponent console, string message)
    {
        foreach (var pilot in console.SubscribedPilots)
        {
            _popup.PopupEntity(message, pilot, pilot);
        }
    }

    private void OnCargoExamined(Entity<WFCargoMassComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.PerUnit > 0f)
        {
            args.PushMarkup(Loc.GetString("wf-cargo-load-examine-stack", ("tonnes", MathF.Round(Weight(ent, ent.Comp), 2))));
            return;
        }

        var cls = ClassOf(ent, ent.Comp);
        args.PushMarkup(Loc.GetString("wf-cargo-load-examine", ("class", Loc.GetString($"wf-cargo-load-class-{cls.ToLowerInvariant()}")),
            ("tonnes", Weight(ent, ent.Comp))));
    }

    private void OnStorageExamined(Entity<MaterialStorageComponent> ent, ref ExaminedEvent args)
    {
        var weight = StoredWeight(ent.Comp);
        if (weight >= 0.05f)
            args.PushMarkup(Loc.GetString("wf-cargo-load-examine-stored", ("tonnes", MathF.Round(weight, 2)),
                ("percent", (int) MathF.Round(Profile.StoredFactor * 100f))));
    }

    private void OnHelmExamined(Entity<WFCargoHelmComponent> ent, ref ExaminedEvent args)
    {
        if (Transform(ent).GridUid is not { } grid || !TryComp<WFCargoLoadComponent>(grid, out var load))
            return;

        var percent = (int) MathF.Round((1f - load.SpaceFactor) * 100f);
        var spool = _shuttle.TryGetFTLDrive(grid, out _, out var drive) ? SpoolPenalty(grid, drive) : 0f;
        args.PushMarkup(Loc.GetString(load.SpaceFactor <= 0.6f ? "wf-cargo-load-helm-heavy" : "wf-cargo-load-helm",
            ("tonnes", (int) MathF.Round(load.SetLoad)), ("percent", percent), ("spool", (int) MathF.Round(spool))));
    }
}
