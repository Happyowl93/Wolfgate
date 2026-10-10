using System.Linq;
using Content.Server.Destructible;
using Content.Shared._Mono.ArmorPiercing;
using Content.Shared._WF.ShipArmor;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._WF.ShipArmor;

/// <summary>
/// Moves part of the explosion and projectile damage taken by armor-backed mounts onto adjacent hull armor.
/// </summary>
public sealed class WFArmorBackedSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private EntityQuery<ArmorThicknessComponent> _armorQuery;
    private EntityQuery<DamageableComponent> _damageableQuery;
    private EntityQuery<DestructibleComponent> _destructibleQuery;
    private EntityQuery<ProjectileComponent> _projectileQuery;
    private bool _redirecting;

    public override void Initialize()
    {
        base.Initialize();
        _armorQuery = GetEntityQuery<ArmorThicknessComponent>();
        _damageableQuery = GetEntityQuery<DamageableComponent>();
        _destructibleQuery = GetEntityQuery<DestructibleComponent>();
        _projectileQuery = GetEntityQuery<ProjectileComponent>();
        SubscribeLocalEvent<WFArmorBackedComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
        SubscribeLocalEvent<WFArmorBackedComponent, ExaminedEvent>(OnExamined);
    }

    private void OnBeforeDamage(Entity<WFArmorBackedComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (_redirecting || args.Cancelled)
            return;

        // Healing or mixed damage is never shared with armor.
        if (args.Damage.DamageDict.Values.Any(v => v < FixedPoint2.Zero))
            return;

        var transfer = GetTransfer(ent.Comp, args);
        if (transfer <= 0f)
            return;

        if (FindBacking(ent) is not { } backing)
            return;

        var share = transfer * Coverage(ent.Comp, backing.Comp.Thickness);
        if (share <= 0f)
            return;

        // Explosions skip global modifiers in TryChangeDamage, so the re-applied halves do the same.
        var ignoreGlobal = args.OriginFlag == DamageableSystem.DamageOriginFlag.Explosion;

        _redirecting = true;
        try
        {
            _damageable.TryChangeDamage(backing, args.Damage * share, args.IgnoreResistances, interruptsDoAfters: false,
                origin: args.Origin, ignoreGlobalModifiers: ignoreGlobal, tool: args.Tool, originFlag: args.OriginFlag);
            args.Applied = _damageable.TryChangeDamage(ent, args.Damage * (1f - share), args.IgnoreResistances, args.InterruptsDoAfters,
                origin: args.Origin, ignoreGlobalModifiers: ignoreGlobal, armorPenetration: args.ArmorPenetration,
                partMultiplier: args.PartMultiplier, targetPart: args.TargetPart, tool: args.Tool, originFlag: args.OriginFlag);
        }
        finally
        {
            _redirecting = false;
        }

        args.Cancelled = true;
    }

    /// <summary>
    /// Returns the share of this hit that the mount hands to armor, before thickness scaling.
    /// </summary>
    private float GetTransfer(WFArmorBackedComponent comp, BeforeDamageChangedEvent args)
    {
        if (args.OriginFlag == DamageableSystem.DamageOriginFlag.Explosion)
            return comp.ExplosionTransfer;

        if (args.Tool is { } tool && _projectileQuery.TryComp(tool, out var projectile))
            return projectile.PenetrationThreshold > FixedPoint2.Zero ? comp.PenetratingTransfer : comp.ProjectileTransfer;

        return 0f;
    }

    /// <summary>
    /// Fraction of full protection a backing armor of this thickness provides.
    /// </summary>
    private static float Coverage(WFArmorBackedComponent comp, int thickness)
    {
        return Math.Clamp(thickness / (float) comp.FullThickness, 0f, 1f);
    }

    /// <summary>
    /// Finds the thickest damageable armor anchored within the mount's search radius.
    /// Ties go to the least damaged armor.
    /// </summary>
    private Entity<ArmorThicknessComponent>? FindBacking(Entity<WFArmorBackedComponent> ent)
    {
        var xform = Transform(ent.Owner);
        if (!xform.Anchored || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var origin = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        Entity<ArmorThicknessComponent>? best = null;
        var bestDamage = FixedPoint2.Zero;

        for (var x = -ent.Comp.Radius; x <= ent.Comp.Radius; x++)
        {
            for (var y = -ent.Comp.Radius; y <= ent.Comp.Radius; y++)
            {
                var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, origin + new Vector2i(x, y));
                while (anchored.MoveNext(out var maybeUid))
                {
                    if (maybeUid is not { } uid || uid == ent.Owner || HasComp<WFArmorBackedComponent>(uid))
                        continue;

                    // Indestructible armor would make the mount nearly immune, so it is not a backing.
                    if (!_armorQuery.TryComp(uid, out var armor) || !_damageableQuery.TryComp(uid, out var damageable) ||
                        !_destructibleQuery.HasComp(uid))
                        continue;

                    var total = damageable.TotalDamage;
                    if (best is not { } current ||
                        armor.Thickness > current.Comp.Thickness ||
                        (armor.Thickness == current.Comp.Thickness && total < bestDamage))
                    {
                        best = new Entity<ArmorThicknessComponent>(uid, armor);
                        bestDamage = total;
                    }
                }
            }
        }

        return best;
    }

    private void OnExamined(Entity<WFArmorBackedComponent> ent, ref ExaminedEvent args)
    {
        if (FindBacking(ent) is not { } backing)
        {
            args.PushMarkup(Loc.GetString("wf-armor-backed-examine-none"));
            return;
        }

        var coverage = Coverage(ent.Comp, backing.Comp.Thickness);
        args.PushMarkup(Loc.GetString("wf-armor-backed-examine",
            ("blast", (int) MathF.Round(ent.Comp.ExplosionTransfer * coverage * 100f)),
            ("shot", (int) MathF.Round(ent.Comp.ProjectileTransfer * coverage * 100f))));
    }
}
