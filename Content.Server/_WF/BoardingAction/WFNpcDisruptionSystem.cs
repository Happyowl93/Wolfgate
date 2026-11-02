using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.BoardingAction;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;

namespace Content.Server._WF.BoardingAction;

/// <summary>
/// NPC ships hold what they are shooting at: an encounter ship or a hostile drone disrupts any non-encounter ship it
/// has guns on, within range.
/// </summary>
public sealed partial class WFNpcDisruptionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFFtlDisruptionSystem _disruption = default!;
    [Dependency] private WFGunnerDutySystem _gunners = default!;

    /// <summary>Seconds between passes over the NPC ships' targets.</summary>
    private const float CheckInterval = 1f;

    private readonly HashSet<(EntityUid Ship, EntityUid Target)> _held = new();
    private readonly HashSet<(EntityUid Ship, EntityUid Target)> _wanted = new();
    private float _accumulator;
    private float _range;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        Subs.CVar(_config, WFBoardingCVars.NpcDisruptRange, value => _range = value, true);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _held.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator < CheckInterval)
            return;

        _accumulator = 0f;
        _wanted.Clear();

        var gunners = EntityQueryEnumerator<WFGunnerDutyComponent, TransformComponent>();
        while (gunners.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } ship || !HasComp<WFEncounterGridComponent>(ship))
                continue;

            foreach (var (target, _) in _gunners.Batteries(uid))
            {
                Want(ship, target);
            }
        }

        var aimers = EntityQueryEnumerator<ShipTargetingComponent, TransformComponent>();
        while (aimers.MoveNext(out var uid, out var aim, out var xform))
        {
            if (xform.GridUid is not { } ship || !aim.Target.EntityId.IsValid() ||
                HasComp<WFCrewComponent>(uid) && !HasComp<WFEncounterGridComponent>(ship))
                continue;

            Want(ship, aim.Target.EntityId);
        }

        _held.RemoveWhere(pair =>
        {
            if (_wanted.Contains(pair))
                return false;

            _disruption.Release(pair.Target, pair.Ship);
            return true;
        });

        foreach (var pair in _wanted)
        {
            if (_held.Add(pair))
                _disruption.Disrupt(pair.Target, pair.Ship, pair.Ship);
        }
    }

    /// <summary>Adds a ship and whatever it is shooting at, if that is a ship in range it can hold.</summary>
    private void Want(EntityUid ship, EntityUid target)
    {
        if (TerminatingOrDeleted(target))
            return;

        // NPC ships leave their own fights by jumping, so they never pin each other.
        var hull = Transform(target).GridUid ?? target;
        if (hull == ship || HasComp<WFEncounterGridComponent>(hull) || Transform(hull).MapID != Transform(ship).MapID ||
            !_disruption.IsTackleable(hull) || _disruption.DistanceToHull(_transform.GetWorldPosition(ship), hull) > _range)
            return;

        _wanted.Add((ship, hull));
    }
}
