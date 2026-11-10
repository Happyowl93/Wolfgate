using Content.Server.Stack;
using Content.Shared._WF.Cyborgs;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._WF.Cyborgs;

/// <summary>
/// Restocks <see cref="WFBorgStackRefillComponent"/> stacks, one unit at a time, while their cyborg is in a charger.
/// </summary>
public sealed partial class WFBorgStackRefillSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private StackSystem _stack = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        RefillAll();
    }

    /// <summary>
    /// Adds one unit to every refilling stack that is short and aboard a charging cyborg.
    /// </summary>
    public void RefillAll()
    {
        var query = EntityQueryEnumerator<WFBorgStackRefillComponent, StackComponent>();
        while (query.MoveNext(out var uid, out var refill, out var stack))
        {
            if (stack.Count < refill.Target && IsCharging(uid))
                _stack.SetCount(uid, stack.Count + 1, stack);
        }
    }

    /// <summary>
    /// Whether the stack is held by, or stowed in a module of, a cyborg inside a powered charger.
    /// </summary>
    private bool IsCharging(EntityUid stack)
    {
        // In hand the stack's parent is the chassis; stowed, it is the module, whose parent is the chassis.
        var chassis = Transform(stack).ParentUid;
        if (!HasComp<BorgChassisComponent>(chassis))
            chassis = chassis.IsValid() ? Transform(chassis).ParentUid : EntityUid.Invalid;

        if (!HasComp<BorgChassisComponent>(chassis))
            return false;

        return _container.TryGetContainingContainer((chassis, null, null), out var container)
            && TryComp<ChargerComponent>(container.Owner, out var charger)
            && charger.Status is CellChargerStatus.Charging or CellChargerStatus.Charged;
    }
}
