using Content.Server._WG.NpcCrew.Components;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared._WG.NpcCrew;
using Content.Shared.NPC.Components;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>
/// Gates the fight branch by the crewman's engagement rule. On-sight crew may always fight (the combat compounds
/// still need a target); when-attacked crew only while they remember being attacked.
/// </summary>
public sealed partial class WGCrewMayFightPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_entManager.TryGetComponent<WGCrewComponent>(owner, out var passive) && passive.Engagement == WGCrewEngagement.Never)
            return false;
        if (!_entManager.System<Content.Server._WG.NpcCrew.Systems.WGCrewWeaponSystem>().HasLiveThreat(owner))
            return false;
        if (_entManager.System<Content.Server._WG.NpcCrew.Systems.WGCrewSecuritySystem>().HasThreat(owner))
            return true;
        if (!_entManager.TryGetComponent<WGCrewComponent>(owner, out var crew)
            || crew.Engagement == WGCrewEngagement.OnSight)
        {
            return true;
        }

        if (_entManager.TryGetComponent<NPCRetaliationComponent>(owner, out var retaliation)
            && retaliation.AttackMemoryLength != null)
        {
            foreach (var expiry in retaliation.AttackMemories.Values)
            {
                if (_timing.CurTime < expiry)
                    return true;
            }

            return false;
        }

        // No memory window: provoked for as long as someone is on the aggro list.
        return _entManager.TryGetComponent<FactionExceptionComponent>(owner, out var exceptions)
               && exceptions.Hostiles.Count > 0;
    }
}
