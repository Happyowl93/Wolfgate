using Content.Server._WG.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Content.Shared.Examine;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    /// <summary>Reads operating specifications from the selected emitter's runtime component.</summary>
    public WGShipShieldGeneratorStats GetWolfgateGeneratorStats(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        var cost = CompOrNull<WGShipShieldCostComponent>(uid);
        return new WGShipShieldGeneratorStats(Name(uid),
            WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp),
            emitter.DamageLimit, emitter.HealPerSecond, emitter.HealPerSecond * emitter.UnpoweredBonus,
            emitter.BaseDraw, emitter.BaseDraw + emitter.MaxDraw, emitter.DamageOverloadTimePunishment)
        {
            ThrustFactor = cost == null ? 1f : Math.Clamp(cost.ThrustFactor, MinShieldMobility, 1f),
            TurnFactor = cost == null ? 1f : Math.Clamp(cost.TurnFactor, MinShieldMobility, 1f),
            GunLockSeconds = MathF.Max(0f, cost?.GunLockout ?? 0f),
            FireHeat = MathF.Max(0f, cost?.FireHeat ?? 0f),
            Vents = cost != null,
        };
    }

    /// <summary>Shows this generator's specifications before or after installation.</summary>
    private void ExamineWolfgateGeneratorStats(EntityUid uid, ShipShieldEmitterComponent emitter, ExaminedEvent args)
    {
        var stats = GetWolfgateGeneratorStats(uid, emitter);
        args.PushMarkup(Loc.GetString("wg-shield-examine-capacity", ("capacity", MathF.Round(stats.Capacity, 0)), ("overload", MathF.Round(stats.DamageLimit, 0))));
        if (stats.Capacity < stats.DamageLimit)
            args.PushMarkup(Loc.GetString("wg-shield-examine-capacity-note"));
        args.PushMarkup(Loc.GetString(stats.Vents ? "wg-shield-examine-cooling" : "wg-shield-examine-recharge",
            ("online", MathF.Round(stats.HealPerSecond, 1)), ("offline", MathF.Round(stats.RechargePerSecond, 1)),
            ("hold", WGShipShieldEffects.HardHeatHold)));
        args.PushMarkup(Loc.GetString("wg-shield-examine-power", ("idle", MathF.Round(stats.BasePowerWatts / 1000f, 1)), ("maximum", MathF.Round(stats.MaximumPowerWatts / 1000f, 1))));
        args.PushMarkup(Loc.GetString("wg-shield-examine-overload", ("seconds", MathF.Round(stats.OverloadSeconds, 1))));
        if (stats.ThrustFactor < 1f || stats.TurnFactor < 1f)
        {
            args.PushMarkup(Loc.GetString("wg-shield-examine-cost",
                ("thrust", MathF.Round((1f - stats.ThrustFactor) * 100f)), ("turn", MathF.Round((1f - stats.TurnFactor) * 100f))));
        }
        if (stats.FireHeat > 0f)
            args.PushMarkup(Loc.GetString("wg-shield-examine-fire-heat", ("percent", MathF.Round(stats.FireHeat * 100f))));
        if (stats.GunLockSeconds > 0f)
            args.PushMarkup(Loc.GetString("wg-shield-examine-gun-lock", ("seconds", MathF.Round(stats.GunLockSeconds, 1))));
    }
}
