using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    /// <summary>
    /// Ends the burst <see cref="AttemptShots"/> keeps firing, and a burst already under way, so a gun told to hold fire
    /// doesn't go on shooting at the aim it was last given. A turret switched to fire on its own is left switched on.
    /// </summary>
    public void WgStopShots(EntityUid gun)
    {
        if (TryComp<AutoShootGunComponent>(gun, out var auto))
            auto.RemainingTime = TimeSpan.Zero;

        if (!TryComp<GunComponent>(gun, out var comp) || !comp.BurstActivated)
            return;

        comp.BurstActivated = false;
        comp.BurstShotsCount = 0;
        comp.ShotCounter = 0;
        Dirty(gun, comp);
    }
}
