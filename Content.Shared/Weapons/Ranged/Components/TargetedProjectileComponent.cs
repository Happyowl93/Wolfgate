using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Ranged.Components;

// WOLFGATE(Performance): was AutoGenerateComponentState; WFDeletedRefStateSystem sends Target, which can be deleted first
[RegisterComponent, NetworkedComponent]
// WOLFGATE(Xenonids): the xeno projectile system sets the target of its shots
[Access(typeof(SharedGunSystem), typeof(Content.Shared._WF.Xenonids.Projectile.XenoProjectileSystem), typeof(Content.Shared._WF.Performance.WFDeletedRefStateSystem))]
public sealed partial class TargetedProjectileComponent : Component
{
    [DataField] // WOLFGATE(Performance): networked by WFDeletedRefStateSystem
    public EntityUid Target;
}
