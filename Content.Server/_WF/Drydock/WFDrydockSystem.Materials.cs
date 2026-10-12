using System.Globalization;
using System.Linq;
using Content.Shared._WF.Drydock;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    [Dependency] private SharedOreSiloSystem _oreSilo = default!;

    private float _materialRefresh;

    /// <summary>Sets each item's material bill for a console that pays in materials.</summary>
    private void AssignMaterials(WFDrydockMaterialPaymentComponent pay, List<WFDrydockWorkItem> items)
    {
        foreach (var item in items)
        {
            Dictionary<ProtoId<MaterialPrototype>, int>? own = null;
            if (item.Kind == WFDrydockWorkKind.Upgrade && item.Upgrade != null &&
                _prototype.TryIndex<WFDrydockUpgradePrototype>(item.Upgrade, out var proto) &&
                item.Tier >= 1 && item.Tier <= proto.Tiers.Count)
            {
                own = proto.Tiers[item.Tier - 1].Materials;
            }

            item.Materials = ItemBill(pay, item.Cost, own);
        }
    }

    /// <summary>A work item's material bill: its tier's per-part bill, or its cost times each rate.</summary>
    private static Dictionary<string, int> ItemBill(WFDrydockMaterialPaymentComponent pay, int cost, Dictionary<ProtoId<MaterialPrototype>, int>? own)
    {
        var bill = new Dictionary<string, int>();
        if (own is { Count: > 0 })
        {
            foreach (var (id, amount) in own)
                bill[id] = amount;
            return bill;
        }

        foreach (var (id, rate) in pay.Rates)
        {
            var amount = (int) Math.Ceiling(cost * rate);
            if (amount > 0)
                bill[id] = amount;
        }

        return bill;
    }

    /// <summary>Adds each material of one bill into another.</summary>
    private static void AddBill(Dictionary<string, int> bill, Dictionary<string, int> add)
    {
        foreach (var (id, amount) in add)
        {
            bill[id] = bill.GetValueOrDefault(id) + amount;
        }
    }

    /// <summary>Sums the material bills of work items.</summary>
    private static Dictionary<string, int> SumBills(IEnumerable<WFDrydockWorkItem> items)
    {
        var bill = new Dictionary<string, int>();
        foreach (var item in items)
        {
            if (item.Materials != null)
                AddBill(bill, item.Materials);
        }

        return bill;
    }

    /// <summary>The cranes a console's next job would use: its working berth cranes, or none without a berth.</summary>
    private List<EntityUid> WorkingCranes(EntityUid console, WFDrydockConsoleComponent comp)
    {
        return TryGetBerth(console, comp, out var berth) ? berth.Cranes : new List<EntityUid>();
    }

    /// <summary>The cranes a job drives, skipping empty slots.</summary>
    private static List<EntityUid> JobCranes(WFDrydockJobComponent job)
    {
        return job.Slots.Where(s => s.Crane != null).Select(s => s.Crane!.Value).ToList();
    }

    /// <summary>The silo a crane draws from, if it is linked to one that can transmit to it.</summary>
    private EntityUid? CraneSilo(EntityUid crane)
    {
        if (CompOrNull<OreSiloClientComponent>(crane)?.Silo is not { } silo || !TryComp<OreSiloComponent>(silo, out var siloComp))
            return null;

        return _oreSilo.CanTransmitMaterials((silo, siloComp, Transform(silo)), crane) ? silo : null;
    }

    /// <summary>The distinct silos that can transmit to at least one of the cranes, in crane order.</summary>
    private List<EntityUid> SourceSilos(List<EntityUid> cranes)
    {
        var silos = new List<EntityUid>();
        foreach (var crane in cranes)
        {
            if (CraneSilo(crane) is { } silo && !silos.Contains(silo))
                silos.Add(silo);
        }

        return silos;
    }

    /// <summary>An entity's own stock, ignoring any linked silo.</summary>
    private Dictionary<string, int> LocalStored(EntityUid uid)
    {
        var stored = new Dictionary<string, int>();
        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return stored;

        foreach (var (id, amount) in _materials.GetStoredMaterials((uid, storage), localOnly: true))
        {
            stored[id] = amount;
        }

        return stored;
    }

    /// <summary>
    /// Materials a console's cranes can pay with: each crane's own stock plus each distinct linked silo once,
    /// so several cranes on one silo don't count it twice.
    /// </summary>
    private Dictionary<string, int> Availability(List<EntityUid> cranes)
    {
        var total = new Dictionary<string, int>();
        foreach (var crane in cranes)
            AddBill(total, LocalStored(crane));

        foreach (var silo in SourceSilos(cranes))
            AddBill(total, LocalStored(silo));

        return total;
    }

    /// <summary>Adds (positive) or takes (negative) materials from one entity's own storage, ignoring linked silos.</summary>
    private bool TryChangeLocal(EntityUid uid, Dictionary<string, int> change)
    {
        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return false;

        return _materials.TryChangeMaterialAmount((uid, storage), change, localOnly: true);
    }

    /// <summary>
    /// Takes a material job's bill from the console's cranes, silos first, or denies with the materials short.
    /// All-or-nothing: nothing is taken unless the whole bill is covered.
    /// </summary>
    private bool TryPayMaterials(EntityUid console, WFDrydockConsoleComponent comp, EntityUid actor, List<WFDrydockWorkItem> items)
    {
        var bill = SumBills(items);
        var cranes = WorkingCranes(console, comp);
        var silos = SourceSilos(cranes);

        // Each source is visited once per material, so no stock is counted or taken twice.
        var draws = new Dictionary<EntityUid, Dictionary<string, int>>();
        var missing = new Dictionary<string, int>();
        foreach (var (id, amount) in bill)
        {
            var left = amount;
            foreach (var source in silos.Concat(cranes))
            {
                if (left <= 0)
                    break;

                var take = Math.Min(left, _materials.GetMaterialAmount(source, id, localOnly: true));
                if (take <= 0)
                    continue;

                if (!draws.TryGetValue(source, out var draw))
                    draws[source] = draw = new Dictionary<string, int>();

                draw[id] = take;
                left -= take;
            }

            if (left > 0)
                missing[id] = left;
        }

        if (missing.Count > 0)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-insufficient-materials", ("missing", FormatMaterials(missing))));

        var applied = new List<(EntityUid Uid, Dictionary<string, int> Draw)>();
        foreach (var (source, draw) in draws)
        {
            if (!TryChangeLocal(source, draw.ToDictionary(p => p.Key, p => -p.Value)))
            {
                foreach (var (uid, undo) in applied)
                    TryChangeLocal(uid, undo);

                return Deny(console, comp, actor, Loc.GetString("drydock-console-transaction-failed"));
            }

            applied.Add((source, draw));
        }

        return true;
    }

    /// <summary>
    /// Puts the material bills of unfinished items back into the job's first crane, through its linked silo when it
    /// can transmit, and returns the bill.
    /// </summary>
    private Dictionary<string, int> ReturnMaterials(EntityUid console, List<EntityUid> cranes, List<WFDrydockWorkItem> items)
    {
        var bill = SumBills(items);
        if (bill.Count == 0)
            return bill;

        var crane = cranes.FirstOrDefault(c => HasComp<MaterialStorageComponent>(c));
        if (!crane.Valid || !TryChangeLocal(CraneSilo(crane) ?? crane, bill))
        {
            _sawmill.Warning($"Could not return drydock materials to {ToPrettyString(console)}; its cranes or silo are gone or full.");
        }

        return bill;
    }

    /// <summary>Re-sends open material consoles' state about once a second, since crane and silo stock change off the console.</summary>
    private void RefreshMaterialConsoles(float frameTime)
    {
        _materialRefresh += frameTime;
        if (_materialRefresh < 1f)
            return;

        _materialRefresh = 0f;
        var query = EntityQueryEnumerator<WFDrydockConsoleComponent, WFDrydockMaterialPaymentComponent>();
        while (query.MoveNext(out var uid, out var comp, out _))
        {
            if (_ui.IsUiOpen(uid, WFDrydockConsoleUiKey.Key))
                UpdateUi(uid, comp);
        }
    }

    /// <summary>Lists a material bill as localized names and sheet amounts, comma separated.</summary>
    private string FormatMaterials(Dictionary<string, int> bill)
    {
        var entries = new List<string>();
        foreach (var (id, amount) in bill)
        {
            var proto = _prototype.Index<MaterialPrototype>(id);
            var sheets = amount / (float) _materials.GetSheetVolume(proto);
            entries.Add(Loc.GetString("drydock-console-material-amount",
                ("name", Loc.GetString(proto.Name)),
                ("sheets", sheets.ToString("0.##", CultureInfo.InvariantCulture))));
        }

        return string.Join(", ", entries);
    }
}
