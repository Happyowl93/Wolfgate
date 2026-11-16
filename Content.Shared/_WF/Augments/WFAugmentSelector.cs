using System.Linq;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._WF.Augments;

/// <summary>
/// Picks one scavenged augment, weighted by <see cref="WFAugmentRules.ScavengeWeight"/>: the bare part or organ, or
/// the autosurgeon for an upgrade.
/// </summary>
public sealed partial class WFAugmentSelector : EntityTableSelector
{
    protected override IEnumerable<EntProtoId> GetSpawnsImplementation(IRobustRandom rand,
        IEntityManager entMan,
        IPrototypeManager proto,
        EntityTableContext ctx)
    {
        var loot = Loot(proto);
        var roll = rand.NextFloat() * loot.Sum(l => l.Weight);
        foreach (var (id, weight) in loot)
        {
            roll -= weight;
            if (roll > 0)
                continue;

            yield return id;
            yield break;
        }
    }

    protected override IEnumerable<(EntProtoId spawn, double)> ListSpawnsImplementation(IEntityManager entMan,
        IPrototypeManager proto,
        EntityTableContext ctx)
    {
        var loot = Loot(proto);
        var total = loot.Sum(l => (double) l.Weight);
        return loot.Select(l => (l.Id, l.Weight / total));
    }

    private static List<(EntProtoId Id, float Weight)> Loot(IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<WFAugmentPrototype>()
            .Where(a => WFAugmentRules.ScavengeWeight(a) > 0)
            .OrderBy(a => a.ID)
            .Select(a => (WFAugmentRules.Installed(a)!.Value, WFAugmentRules.ScavengeWeight(a)))
            .ToList();
    }
}
