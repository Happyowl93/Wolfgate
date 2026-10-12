using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Hailing;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Hailing;

/// <summary>
/// Runs the NPCs that answer hails: each answers after its delay, greets the caller and opens its grid's hail services.
/// </summary>
public sealed class HailResponderSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private HailingSystem _hailing = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;

        var query = EntityQueryEnumerator<HailResponderComponent>();

        while (query.MoveNext(out var uid, out var responder))
        {
            if (TerminatingOrDeleted(uid) || _mobState.IsIncapacitated(uid))
                continue;

            if (FindConsole(uid, responder) is not { } console)
                continue;

            // Keeps the responder on the console; the call ends when its operator closes it.
            _ui.OpenUi(console, ShuttleConsoleUiKey.Key, uid);

            if (!TryComp<HailConsoleComponent>(console, out var hail))
            {
                responder.Greeted = false;
                continue;
            }

            switch (hail.State)
            {
                case HailState.Ringing:
                    if (now >= hail.StateTime + TimeSpan.FromSeconds(responder.AnswerDelay))
                        _hailing.Answer(console, uid, true);

                    break;

                case HailState.Connected:
                    if (!responder.Greeted)
                    {
                        responder.Greeted = true;
                        Greet(uid, console, responder, hail);
                    }

                    break;
            }
        }
    }

    /// <summary>Greets the caller, then raises each hail service on the grid and speaks what they add.</summary>
    private void Greet(EntityUid uid, EntityUid console, HailResponderComponent responder, HailConsoleComponent hail)
    {
        if (hail.Peer is not { } helm)
            return;

        var grid = Transform(console).GridUid;
        if (responder.Greetings.Count > 0)
        {
            // Each side is named as its radar shows the other, so a ship running dark stays unnamed.
            _hailing.Say(console, uid, Loc.GetString(_random.Pick(responder.Greetings),
                ("name", Identity.Name(uid, EntityManager)),
                ("location", grid is { } station ? _hailing.ShipLabel(helm, station) : Loc.GetString("hail-unknown-vessel")),
                ("ship", hail.PeerGrid is { } ship ? _hailing.ShipLabel(console, ship) : Loc.GetString("hail-unknown-vessel"))));
        }

        var lines = new List<string>();
        var query = EntityQueryEnumerator<HailServiceComponent, TransformComponent>();

        while (query.MoveNext(out var service, out _, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            var ev = new HailAnsweredEvent(hail.PeerGrid, lines);
            RaiseLocalEvent(service, ref ev);
        }

        foreach (var line in lines)
        {
            _hailing.Say(console, uid, line);
        }
    }

    /// <summary>Keeps the console it mans if still valid, otherwise takes the nearest powered one in range.</summary>
    private EntityUid? FindConsole(EntityUid uid, HailResponderComponent responder)
    {
        if (responder.Console is { } cached && IsManned(uid, cached, responder.ConsoleRange))
            return cached;

        responder.Console = null;

        if (Transform(uid).GridUid is not { } grid)
            return null;

        var origin = _transform.GetWorldPosition(uid);
        var nearest = responder.ConsoleRange;
        var query = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();

        while (query.MoveNext(out var console, out _, out var xform))
        {
            if (xform.GridUid != grid || !this.IsPowered(console, EntityManager))
                continue;

            var distance = (_transform.GetWorldPosition(console) - origin).Length();

            if (distance > nearest)
                continue;

            nearest = distance;
            responder.Console = console;
        }

        return responder.Console;
    }

    /// <summary>Whether the console is still powered, on the responder's grid, and in range.</summary>
    private bool IsManned(EntityUid uid, EntityUid console, float range)
    {
        if (TerminatingOrDeleted(console) || !HasComp<ShuttleConsoleComponent>(console) ||
            Transform(console).GridUid != Transform(uid).GridUid || !this.IsPowered(console, EntityManager))
        {
            return false;
        }

        return (_transform.GetWorldPosition(console) - _transform.GetWorldPosition(uid)).Length() <= range;
    }
}
