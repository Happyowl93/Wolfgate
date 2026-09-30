using Content.Server.Shuttles.Events;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;

namespace Content.Server._Mono.Detection;

public sealed partial class ApplyIFFFlagsToDockedShipsSystem : EntitySystem
{
    [Dependency] private SharedShuttleSystem _shuttle = default!;
    public override void Initialize()
    {
        base.Initialize();

        // We will run the event if dock A has the component.
        SubscribeLocalEvent<DockEvent>(OnDock);
        SubscribeLocalEvent<UndockEvent>(OnUndock);
    }

    private void OnDock(DockEvent args)
    {
        // WOLFGATE(Shuttles) START: the host can be grid B, since docking orders the pair by entity id.
        if (TryComp<ApplyIFFFlagsToDockedShipsComponent>(args.GridBUid, out var hostB))
            ApplyFlags(args.GridAUid, hostB, true);
        // WOLFGATE END

        if (!TryComp<ApplyIFFFlagsToDockedShipsComponent>(args.GridAUid, out var iffComp))
            return;

        ApplyFlags(args.GridBUid, iffComp, true);
    }

    private void OnUndock(UndockEvent args)
    {
        // WOLFGATE(Shuttles) START: the host can be grid B, since grid A is the side that undocked, usually the ship.
        if (TryComp<ApplyIFFFlagsToDockedShipsComponent>(args.GridBUid, out var hostB))
            ApplyFlags(args.GridAUid, hostB, false);
        // WOLFGATE END

        if (!TryComp<ApplyIFFFlagsToDockedShipsComponent>(args.GridAUid, out var iffComp))
            return;

        ApplyFlags(args.GridBUid, iffComp, false);
    }

    public void ApplyFlags(EntityUid gridUid, ApplyIFFFlagsToDockedShipsComponent iffComp, bool applying = true)
    {
        // WOLFGATE(Shuttles) START: a ship loses only flags hosts added, once no host it is still docked to provides them.
        ApplyTrackedFlags(gridUid, iffComp, applying);
        // if (applying)
        //     _shuttle.AddIFFFlag(gridUid, iffComp.Flags);
        // else
        //     _shuttle.RemoveIFFFlag(gridUid, iffComp.Flags);
        // WOLFGATE END
    }
}