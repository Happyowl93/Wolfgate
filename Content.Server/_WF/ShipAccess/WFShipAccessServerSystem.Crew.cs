using System.Linq;
using Content.Shared._WF.ShipAccess;

namespace Content.Server._WF.ShipAccess;

public sealed partial class WFShipAccessServerSystem
{
    /// <summary>Replaces automatic crew credentials without changing manually granted access.</summary>
    public void SetCrew(Entity<WFShipAccessComponent> ship, int? id, string? name, IEnumerable<WFShipAccessKey> keys)
    {
        var current = keys.Distinct().ToList();
        if (ship.Comp.CrewId == id && ship.Comp.CrewName == name && ship.Comp.CrewKeys.SequenceEqual(current))
            return;
        ship.Comp.CrewId = id;
        ship.Comp.CrewName = name;
        ship.Comp.CrewKeys = current;
        Dirty(ship);
        RefreshShip(ship);
    }

    /// <summary>Links an unbound ship to the actor's crew through its membership system.</summary>
    public bool TryLinkCrew(Entity<WFShipAccessComponent> ship, EntityUid actor)
    {
        if (ship.Comp.CrewId != null)
            return true;
        var ev = new WFShipCrewLinkEvent(actor);
        RaiseLocalEvent(ship.Owner, ref ev);
        return ship.Comp.CrewId != null;
    }

    /// <summary>Changes ship mode only when its required crew or company exists.</summary>
    public bool SetMode(Entity<WFShipAccessComponent> ship, WFShipAccessMode mode)
    {
        if (!Enum.IsDefined(mode) || mode == WFShipAccessMode.Crew && ship.Comp.CrewId == null ||
            mode == WFShipAccessMode.Faction && !_access.IsFactionGrid(ship, out _))
            return false;
        if (ship.Comp.Mode == mode)
            return true;
        ship.Comp.Mode = mode;
        Dirty(ship);
        RefreshShip(ship);
        return true;
    }
}

/// <summary>Asks the crew membership system to bind a ship to the actor's current crew.</summary>
[ByRefEvent]
public readonly record struct WFShipCrewLinkEvent(EntityUid Actor);
