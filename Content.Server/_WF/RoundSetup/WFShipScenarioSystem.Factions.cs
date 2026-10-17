using System.Linq;
using Content.Server._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.RoundSetup;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFShipScenarioSystem
{
    [Dependency] private IComponentFactory _factionComponents = default!;

    /// <summary>Tier membership comes from the existing T0 and T1 vouchers, including downstream additions.</summary>
    private HashSet<string> FactionVessels(WFCrewFaction faction)
    {
        var vessels = new HashSet<string>();
        var prefix = faction switch { WFCrewFaction.TSF => "Tsf", WFCrewFaction.PDV => "Pdv", _ => null };
        if (prefix == null)
            return vessels;
        foreach (var tier in new[] { 0, 1 })
        {
            if (!_prototypes.TryIndex<EntityPrototype>($"ShipVoucher{prefix}T{tier}", out var prototype) ||
                !prototype.Components.TryGetComponent<ShipyardVoucherComponent>(_factionComponents, out var voucher) ||
                voucher.CompanyName != faction.ToString())
                continue;
            foreach (var id in voucher.Vessels)
                vessels.Add(id.Id);
        }
        return vessels;
    }

    private bool IsFactionVessel(VesselPrototype vessel, WFCrewFaction faction, HashSet<string>? catalog = null) =>
        !vessel.Abstract && vessel.Price > 0 && (catalog ?? FactionVessels(faction)).Contains(vessel.ID) &&
        vessel.Group == (faction == WFCrewFaction.TSF ? ShipyardConsoleUiKey.Security : ShipyardConsoleUiKey.BlackMarket);
}
