using Content.Shared.Drunk;
using Content.Server.Speech.Components;
using Content.Shared.StatusEffect;
using Content.Shared.Traits.Assorted;

namespace Content.Server._WF.Narcotics;

/// <summary>
/// Drunkenness that follows the alcohol in the blood: each metabolism tick holds the drunk timer at the units left
/// times a strength, so a body sobers up as the alcohol clears instead of on a timer of its own.
/// </summary>
public sealed class WFAlcoholSystem : EntitySystem
{
    [Dependency] private StatusEffectsSystem _status = default!;

    private const string SlurKey = "SlurredSpeech";

    /// <summary>How long the slur outlasts the tick that held it.</summary>
    private static readonly TimeSpan SlurTime = TimeSpan.FromSeconds(2.5);

    /// <summary>Raises the drunk timer to at least <paramref name="seconds"/>, leaving any longer one from elsewhere.</summary>
    public void HoldDrunkenness(EntityUid uid, float seconds, bool slur)
    {
        if (!TryComp<StatusEffectsComponent>(uid, out var status))
            return;

        if (TryComp<LightweightDrunkComponent>(uid, out var trait))
            seconds *= trait.BoozeStrengthMultiplier;

        if (seconds <= 0f)
            return;

        _status.TryAddStatusEffect<DrunkComponent>(uid, SharedDrunkSystem.DrunkKey, TimeSpan.FromSeconds(seconds), true, status);
        if (slur)
            _status.TryAddStatusEffect<SlurredAccentComponent>(uid, SlurKey, SlurTime, true, status);
    }
}
