using Content.Shared._White.Standing;
using Content.Shared.CCVar;
using Content.Shared.Stunnable;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._WF.Standing;

/// <summary>
/// Decides whether a knocked-down body gets up by itself: its own player's setting, or always when it has no player.
/// </summary>
public sealed class AutoGetUpSystem : EntitySystem
{
    [Dependency] private INetConfigurationManager _cfg = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KnockedDownComponent, ComponentStartup>(OnKnockedDown);
    }

    private void OnKnockedDown(Entity<KnockedDownComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp(ent, out LayingDownComponent? layingDown))
            return;

        var auto = !_player.TryGetSessionByEntity(ent, out var session) ||
                   _cfg.GetClientCVar(session.Channel, CCVars.AutoGetUp);
        if (layingDown.AutoGetUp == auto)
            return;

        layingDown.AutoGetUp = auto;
        Dirty(ent.Owner, layingDown);
    }
}
