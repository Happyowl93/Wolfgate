using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server._NF.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Construction.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Finds drydock-refittable machines on a ship and swaps their stock machine parts for higher-rated ones.
/// </summary>
public sealed partial class WFDrydockUpgradeSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ConstructionSystem _construction = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _factory = default!;

    /// <summary>Replacement part prototype for a (part type, rating) pair.</summary>
    private readonly Dictionary<(string Part, int Rating), EntProtoId> _replacements = new();

    /// <summary>Part pairs already reported as missing a replacement, so each is logged once.</summary>
    private readonly HashSet<(string Part, int Rating)> _warned = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        RebuildReplacements();
    }

    /// <summary>Whether an entity is a machine of the category an upgrade applies to.</summary>
    private bool IsPart(EntityUid uid, WFDrydockUpgradePrototype proto)
    {
        if (!TryComp<MachineComponent>(uid, out var machine) || !HasMachineParts(machine))
            return false;

        switch (proto.Target)
        {
            case WFDrydockUpgradeTarget.LinearThruster:
                return TryComp<ThrusterComponent>(uid, out var linear) && linear.Type == ThrusterType.Linear;
            case WFDrydockUpgradeTarget.Gyroscope:
                return TryComp<ThrusterComponent>(uid, out var angular) && angular.Type == ThrusterType.Angular;
            case WFDrydockUpgradeTarget.Power:
                return HasComp<UpgradeBatteryComponent>(uid)
                    || HasComp<UpgradePowerSupplierComponent>(uid)
                    || HasComp<UpgradePowerSupplyRampingComponent>(uid);
            default:
                return false;
        }
    }

    /// <summary>Collects the refittable machines of an upgrade category anchored on a ship, clearing the list first.</summary>
    public void GetParts(EntityUid ship, WFDrydockUpgradePrototype proto, List<EntityUid> parts)
    {
        parts.Clear();

        if (!TryComp<MapGridComponent>(ship, out var grid))
            return;

        var machines = new HashSet<Entity<MachineComponent>>();
        _lookup.GetLocalEntitiesIntersecting(ship, grid.LocalAABB, machines);
        foreach (var ent in machines)
        {
            if (IsPart(ent.Owner, proto) && IsAnchoredOn(ent.Owner, ship))
                parts.Add(ent.Owner);
        }
    }

    /// <summary>Lowest machine part rating in a machine; 1 when it has no parts.</summary>
    private int GetRating(EntityUid machine)
    {
        if (!TryComp<MachineComponent>(machine, out var comp))
            return 1;

        var rating = int.MaxValue;
        var found = false;
        foreach (var part in comp.PartContainer.ContainedEntities)
        {
            if (!TryComp<MachinePartComponent>(part, out var partComp))
                continue;

            found = true;
            rating = Math.Min(rating, partComp.Rating);
        }

        return found ? rating : 1;
    }

    /// <summary>Current refit tier of a machine: how many leading tiers its parts already meet.</summary>
    public int GetTier(EntityUid machine, WFDrydockUpgradePrototype proto)
    {
        var rating = GetRating(machine);
        var tier = 0;
        while (tier < proto.Tiers.Count && proto.Tiers[tier].Rating <= rating)
        {
            tier++;
        }

        return tier;
    }

    /// <summary>Raises a machine to a refit tier by swapping its lower-rated parts for the tier's rating.</summary>
    public void ApplyTier(EntityUid machine, WFDrydockUpgradePrototype proto, int tier)
    {
        if (!TryComp<MachineComponent>(machine, out var comp))
            return;

        if (tier <= GetTier(machine, proto) || tier > proto.Tiers.Count)
            return;

        var target = proto.Tiers[tier - 1].Rating;
        var coords = Transform(machine).Coordinates;

        foreach (var part in new List<EntityUid>(comp.PartContainer.ContainedEntities))
        {
            if (!TryComp<MachinePartComponent>(part, out var partComp) || partComp.Rating >= target)
                continue;

            var key = (partComp.PartType.Id, target);
            if (!_replacements.TryGetValue(key, out var replacement))
            {
                if (_warned.Add(key))
                    Log.Warning($"No stock machine part of type {key.Item1} at rating {key.Item2} for drydock refit.");
                continue;
            }

            _container.Remove(part, comp.PartContainer);
            QueueDel(part);

            var spawned = Spawn(replacement, coords);
            _container.Insert(spawned, comp.PartContainer);
        }

        _construction.RefreshParts(machine, comp);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        RebuildReplacements();
    }

    /// <summary>Maps each (part type, rating) to a stock part prototype, preferring "StockPart" IDs for determinism.</summary>
    private void RebuildReplacements()
    {
        _replacements.Clear();
        var machinePartName = _factory.GetComponentName<MachinePartComponent>();

        foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract || !proto.Components.TryGetValue(machinePartName, out var entry))
                continue;

            var part = (MachinePartComponent) entry.Component;
            var key = (part.PartType.Id, part.Rating);
            if (!_replacements.TryGetValue(key, out var existing) || PrefersOver(proto.ID, existing.Id))
                _replacements[key] = proto.ID;
        }
    }

    private static bool PrefersOver(string candidate, string existing)
    {
        var candidateStock = candidate.EndsWith("StockPart", StringComparison.Ordinal);
        var existingStock = existing.EndsWith("StockPart", StringComparison.Ordinal);
        if (candidateStock != existingStock)
            return candidateStock;

        return string.CompareOrdinal(candidate, existing) < 0;
    }

    private bool HasMachineParts(MachineComponent machine)
    {
        foreach (var part in machine.PartContainer.ContainedEntities)
        {
            if (HasComp<MachinePartComponent>(part))
                return true;
        }

        return false;
    }

    private bool IsAnchoredOn(EntityUid uid, EntityUid ship)
    {
        return TryComp(uid, out TransformComponent? xform) && xform.ParentUid == ship && xform.Anchored;
    }
}
