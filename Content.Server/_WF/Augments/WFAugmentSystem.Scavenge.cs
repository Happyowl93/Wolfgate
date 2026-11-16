using Content.Shared._Goobstation.Autosurgeon;
using Content.Shared._WF.Augments;
using Content.Shared.EntityTable;

namespace Content.Server._WF.Augments;

/// <summary>
/// Scavenged cyberware is rare and worth as much as bought cyberware: it spawns with a mark that belongs to nobody,
/// so it sells as loot, or whoever ends a round with it installed owns it as paid for.
/// </summary>
public sealed partial class WFAugmentSystem
{
    [Dependency] private EntityTableSystem _entityTable = default!;

    private void InitializeScavenge()
    {
        SubscribeLocalEvent<WFAugmentScavengeSpawnerComponent, MapInitEvent>(OnScavengeMapInit);
        SubscribeLocalEvent<WFAugmentPaidComponent, AutoSurgeonDoAfterEvent>(OnPaidSurgeonUsed,
            after: new[] { typeof(AutoSurgeonSystem) });
    }

    private void OnScavengeMapInit(Entity<WFAugmentScavengeSpawnerComponent> ent, ref MapInitEvent args)
    {
        var coords = Transform(ent).Coordinates;
        foreach (var proto in _entityTable.GetSpawns(ent.Comp.Table))
        {
            MarkLoot(Spawn(proto, coords));
        }

        QueueDel(ent);
    }

    /// <summary>Marks a loose part, organ or autosurgeon as the paid-for hardware of the augment it installs, owned by nobody.</summary>
    /// <returns>Whether it is a priced augment's.</returns>
    internal bool MarkLoot(EntityUid uid)
    {
        var proto = Prototype(uid);
        foreach (var augment in _proto.EnumeratePrototypes<WFAugmentPrototype>())
        {
            if (augment.Price <= 0 || WFAugmentRules.Installed(augment)?.Id != proto)
                continue;

            EnsureComp<WFAugmentPaidComponent>(uid).Augments[augment.ID] = new WFAugmentMark(null, EntityUid.Invalid);
            return true;
        }

        return false;
    }

    private void OnPaidSurgeonUsed(Entity<WFAugmentPaidComponent> ent, ref AutoSurgeonDoAfterEvent args)
    {
        if (!args.Cancelled && args.Target is { } body && CompOrNull<AutoSurgeonComponent>(ent)?.Used == true)
            PassMark(ent, body);
    }

    /// <summary>A marked autosurgeon that has been used on a body passes its marks to the part it upgraded.</summary>
    internal void PassMark(Entity<WFAugmentPaidComponent> ent, EntityUid body)
    {
        if (!TryComp<AutoSurgeonComponent>(ent, out var surgeon) || _autoSurgeon.WfUpgradeTarget(body, surgeon) is not { } part)
            return;

        var marks = EnsureComp<WFAugmentPaidComponent>(part).Augments;
        foreach (var (id, mark) in ent.Comp.Augments)
        {
            marks.TryAdd(id, mark with { Body = body });
        }

        RemCompDeferred<WFAugmentPaidComponent>(ent);
    }
}
