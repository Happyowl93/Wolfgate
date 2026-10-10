using Content.Shared._White;
using Content.Shared._White.Standing;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server.Standing;

public sealed partial class LayingDownSystem : SharedLayingDownSystem
{
    [Dependency] private INetConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<CheckAutoGetUpEvent>(OnCheckAutoGetUp);
    }

    private void OnCheckAutoGetUp(CheckAutoGetUpEvent ev, EntitySessionEventArgs args)
    {
        var uid = GetEntity(ev.User);

        // WOLFGATE(Standing): a client only answers for its own body
        if (args.SenderSession.AttachedEntity != uid)
            return;

        if (!TryComp(uid, out LayingDownComponent? layingDown))
            return;

        layingDown.AutoGetUp = _cfg.GetClientCVar(args.SenderSession.Channel, CCVars.AutoGetUp);
        Dirty(uid, layingDown);
    }
}
