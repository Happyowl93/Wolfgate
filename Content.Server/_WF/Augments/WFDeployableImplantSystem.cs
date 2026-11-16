using Content.Server._Goobstation.PairedExtendable;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._WF.Augments;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Emp;
using Content.Shared.Hands.Components;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WF.Augments;

/// <summary>
/// Gives a body the action of each deployable implant in its parts, and folds the implant's item out into that
/// side's hand and back. An EMP folds it in and locks it until it wears off.
/// </summary>
public sealed class WFDeployableImplantSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PairedExtendableSystem _extendable = default!;

    public override void Initialize()
    {
        base.Initialize();
        Subscribe<WFToolArmComponent>();
        Subscribe<WFFingerLighterComponent>();
    }

    private void Subscribe<T>() where T : WFDeployableImplantComponent
    {
        SubscribeLocalEvent<T, ComponentInit>((Entity<T> ent, ref ComponentInit _) => Grant(ent, ent.Comp));
        SubscribeLocalEvent<T, ComponentShutdown>((Entity<T> ent, ref ComponentShutdown _) => Revoke(ent.Comp));
        // Raised on the part as it joins or leaves a body, and as it is switched on or off.
        SubscribeLocalEvent<T, BodyPartComponentsModifyEvent>((Entity<T> ent, ref BodyPartComponentsModifyEvent args) =>
        {
            if (args.Add)
                Grant(ent, ent.Comp);
            else
                Revoke(ent.Comp);
        });
        SubscribeLocalEvent<T, WFToggleDeployableImplantEvent>((Entity<T> ent, ref WFToggleDeployableImplantEvent args) => Toggle(ent, ent.Comp, args));
        SubscribeLocalEvent<T, EmpPulseEvent>((Entity<T> ent, ref EmpPulseEvent args) =>
        {
            args.Affected = true;
            args.Disabled = true;
            Retract(ent.Comp);
        });
    }

    private void Grant(EntityUid part, WFDeployableImplantComponent implant)
    {
        if (CompOrNull<BodyPartComponent>(part)?.Body is { } body)
            _actions.AddAction(body, ref implant.ActionEntity, implant.Action, part);
    }

    private void Revoke(WFDeployableImplantComponent implant)
    {
        Retract(implant);
        QueueDel(implant.ActionEntity);
        implant.ActionEntity = null;
    }

    private void Retract(WFDeployableImplantComponent implant)
    {
        QueueDel(implant.Deployed);
        implant.Deployed = null;
    }

    private void Toggle(EntityUid part, WFDeployableImplantComponent implant, WFToggleDeployableImplantEvent args)
    {
        // Every deployable in the part hears the event; only the one whose action it was answers.
        if (args.Action.Owner != implant.ActionEntity
            || !TryComp<BodyPartComponent>(part, out var partComp)
            || partComp.Body is not { } body)
            return;

        if (HasComp<EmpDisabledComponent>(part))
        {
            _popup.PopupEntity(Loc.GetString("wf-augment-deployable-emp"), body, body);
            return;
        }

        var side = partComp.Symmetry switch
        {
            BodyPartSymmetry.Left => HandLocation.Left,
            BodyPartSymmetry.Right => HandLocation.Right,
            _ => HandLocation.Middle,
        };
        args.Handled = _extendable.ToggleExtendable(body, implant.Item, side, out implant.Deployed, implant.Deployed);
        if (args.Handled)
            _audio.PlayPvs(implant.Deployed == null ? implant.RetractSound : implant.DeploySound, part);
    }
}
