using Content.Server.Ame.Components;
using Content.Server.Ame.EntitySystems;
using Content.Server.Popups;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.IreWarnings;

/// <summary>
/// Sounds an alarm and shows a popup to people near an injecting IRE controller whose cores have fallen below
/// <see cref="WarnBelow"/> percent stability, at most once per <see cref="Cooldown"/>.
/// </summary>
public sealed class WGIreWarningSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AmeControllerSystem _ame = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Stability, in percent, below which the controller warns.</summary>
    public const int WarnBelow = 50;

    /// <summary>Distance in metres within which people see the warning.</summary>
    public const float WarnRange = 12f;

    /// <summary>Shortest time between two warnings from one controller.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan CheckPeriod = TimeSpan.FromSeconds(1);
    private static readonly SoundSpecifier AlarmSound = new SoundPathSpecifier("/Audio/Machines/warning_buzzer.ogg");

    private readonly Dictionary<EntityUid, TimeSpan> _nextWarning = new();
    private readonly List<EntityUid> _stale = new();
    private TimeSpan _nextCheck;

    /// <summary>Injection amount up to which the cores take no damage: two per core.</summary>
    public static int SafeInjectionLimit(int coreCount)
    {
        return coreCount * 2;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;
        _nextCheck = now + CheckPeriod;

        var query = EntityQueryEnumerator<AmeControllerComponent>();
        while (query.MoveNext(out var uid, out var controller))
        {
            TryWarn((uid, controller), now);
        }

        _stale.Clear();
        foreach (var uid in _nextWarning.Keys)
        {
            if (TerminatingOrDeleted(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            _nextWarning.Remove(uid);
        }
    }

    /// <summary>Warns around the controller if it is injecting into cores below the threshold and off cooldown.</summary>
    /// <returns>Whether a warning went out.</returns>
    public bool TryWarn(Entity<AmeControllerComponent> ent, TimeSpan now)
    {
        if (!ent.Comp.Injecting
            || !_ame.WGTryGetIreReadout(ent, out var stability, out var safeLimit)
            || stability >= WarnBelow)
        {
            // The cooldown stands even while it isn't injecting, so stopping and restarting can't re-arm the alarm.
            return false;
        }

        if (_nextWarning.TryGetValue(ent, out var next) && now < next)
            return false;

        _nextWarning[ent] = now + Cooldown;

        var message = Loc.GetString("wg-ire-overload-warning", ("stability", stability), ("limit", safeLimit));
        var filter = Filter.Empty().AddInRange(_transform.GetMapCoordinates(ent), WarnRange);
        _popup.PopupEntity(message, ent, filter, true, PopupType.LargeCaution);
        _audio.PlayPvs(AlarmSound, ent);
        return true;
    }
}
