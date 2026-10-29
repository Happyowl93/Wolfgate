using Content.Shared.Clothing;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Companies;

/// <summary>The loadouts a company may issue a rank: those its base job can pick in the character loadout.</summary>
public static class WFCompanyLoadouts
{
    /// <summary>Every loadout in the base job's visible loadout groups, each once.</summary>
    public static List<LoadoutPrototype> Allowed(IPrototypeManager prototypes, string job)
    {
        var result = new List<LoadoutPrototype>();
        if (!prototypes.TryIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job), out var role))
            return result;
        var seenGroups = new HashSet<string>();
        var seen = new HashSet<string>();
        var groups = new List<ProtoId<LoadoutGroupPrototype>>(role.Groups);
        for (var i = 0; i < groups.Count; i++)
        {
            var groupId = groups[i];
            if (!seenGroups.Add(groupId) || !prototypes.TryIndex(groupId, out var group) || group.Hidden)
                continue;
            foreach (var loadout in group.Loadouts)
            {
                if (seen.Add(loadout) && prototypes.TryIndex(loadout, out var proto))
                    result.Add(proto);
            }
            groups.AddRange(group.Subgroups);
        }
        return result;
    }

    /// <summary>Every entity a loadout hands out: its own gear and its starting gear's.</summary>
    public static List<EntProtoId> Items(IPrototypeManager prototypes, LoadoutPrototype loadout)
    {
        var items = new List<EntProtoId>();
        Add(loadout);
        if (loadout.StartingGear is { } gear && prototypes.TryIndex(gear, out var starting))
            Add(starting);
        return items;

        void Add(IEquipmentLoadout source)
        {
            items.AddRange(source.Equipment.Values);
            items.AddRange(source.Inhand);
            foreach (var stored in source.Storage.Values)
                items.AddRange(stored);
        }
    }
}
