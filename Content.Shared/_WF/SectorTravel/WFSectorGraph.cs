using System.Linq;
using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.SectorTravel;

/// <summary>Questions about the region's sectors and the beacon links between them.</summary>
public static class WFSectorGraph
{
    /// <summary>The galaxy distance between two sectors, or zero if either is unknown.</summary>
    public static float Distance(IPrototypeManager prototypes, string from, string to)
    {
        return prototypes.TryIndex<WFSectorPrototype>(from, out var a) && prototypes.TryIndex<WFSectorPrototype>(to, out var b)
            ? Vector2.Distance(a.GalaxyPosition, b.GalaxyPosition)
            : 0f;
    }

    /// <summary>Whether two sectors have beacons pointing at each other.</summary>
    public static bool Linked(IPrototypeManager prototypes, string from, string to)
    {
        return prototypes.TryIndex<WFSectorPrototype>(from, out var a) && prototypes.TryIndex<WFSectorPrototype>(to, out var b)
            && a.Beacons.Exists(beacon => beacon.Link == to)
            && b.Beacons.Exists(beacon => beacon.Link == from);
    }

    /// <summary>
    /// The fewest beacon links between two sectors, skipping links <paramref name="closed"/> rejects, or null if none
    /// connects them.
    /// </summary>
    public static int? Hops(IPrototypeManager prototypes, string from, string to, Func<string, string, bool>? closed = null)
    {
        return Hops(from, to, sector => prototypes.TryIndex<WFSectorPrototype>(sector, out var proto)
            ? proto.Beacons.Select(beacon => beacon.Link.Id).Where(next => Linked(prototypes, sector, next))
            : Enumerable.Empty<string>(), closed);
    }

    /// <summary>
    /// The fewest links between two sectors, given each sector's linked neighbours, skipping links
    /// <paramref name="closed"/> rejects, or null if none connects them.
    /// </summary>
    public static int? Hops(string from, string to, Func<string, IEnumerable<string>> links, Func<string, string, bool>? closed = null)
    {
        if (from == to)
            return 0;

        var seen = new HashSet<string> { from };
        var frontier = new Queue<(string Sector, int Hops)>();
        frontier.Enqueue((from, 0));
        while (frontier.TryDequeue(out var current))
        {
            foreach (var next in links(current.Sector))
            {
                // Only an open link visits a sector, so a closed one never hides it from another way round.
                if (seen.Contains(next) || closed?.Invoke(current.Sector, next) == true)
                    continue;

                if (next == to)
                    return current.Hops + 1;

                seen.Add(next);
                frontier.Enqueue((next, current.Hops + 1));
            }
        }

        return null;
    }

    /// <summary>The home sector, the one on the game ticker's default map.</summary>
    public static WFSectorPrototype? Home(IPrototypeManager prototypes)
    {
        foreach (var sector in prototypes.EnumeratePrototypes<WFSectorPrototype>())
        {
            if (sector.Home)
                return sector;
        }

        return null;
    }
}
