using Content.Shared.ActionBlocker;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;

namespace Content.Shared._WF.Augments;

/// <summary>Holds a body still while its halo orbs are out, on both sides so the client doesn't predict a step.</summary>
public sealed partial class WFHaloArmingSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private PullingSystem _pulling = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFHaloArmingComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFHaloArmingComponent, ComponentShutdown>((uid, _, _) => _blocker.UpdateCanMove(uid));
        SubscribeLocalEvent<WFHaloArmingComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
    }

    private void OnStartup(Entity<WFHaloArmingComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<PullableComponent>(ent, out var pullable))
            _pulling.TryStopPull(ent, pullable);

        _blocker.UpdateCanMove(ent);
    }

    private void OnUpdateCanMove(Entity<WFHaloArmingComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.Cancel();
    }
}
