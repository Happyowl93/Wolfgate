using Content.Server._NF.Bank;
using Content.Server._WF.SectorTravel.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Construction;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Ships;
using Content.Shared._WF.SectorTravel;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WF.SectorTravel;

/// <summary>
/// Jump fuel: every FTL drive starts with a full tank and beacon jumps spend it. A fusion fuel crate used on a fuel intake
/// (or on the drive itself) refills it, and a ship docked to a station that sells fusion fuel can buy it there at the
/// station's price, out of the station's stock.
/// </summary>
public sealed class WFJumpFuelSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;

    private static readonly TimeSpan ServiceInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextService;

    /// <summary>Drives just made by construction, before their map init: they start with an empty tank.</summary>
    private readonly HashSet<EntityUid> _built = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FTLDriveComponent, MapInitEvent>(OnDriveMapInit);
        SubscribeLocalEvent<ConstructionChangeEntityEvent>(OnConstructed);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _built.Clear());
        SubscribeLocalEvent<WFJumpFuelComponent, InteractUsingEvent>(OnDriveInteractUsing);
        SubscribeLocalEvent<WFJumpFuelComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFJumpFuelIntakeComponent, InteractUsingEvent>(OnIntakeInteractUsing);
        SubscribeLocalEvent<WFJumpFuelIntakeComponent, ExaminedEvent>(OnIntakeExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextService)
            return;

        _nextService = _timing.CurTime + ServiceInterval;
        UpdateServices();
    }

    /// <summary>A drive that comes with a ship or station starts full; one built or rebuilt by hand starts empty.</summary>
    private void OnDriveMapInit(Entity<FTLDriveComponent> ent, ref MapInitEvent args)
    {
        var tank = EnsureComp<WFJumpFuelComponent>(ent);
        tank.Fuel = _built.Remove(ent) ? 0f : tank.Capacity;
    }

    /// <summary>Notes a drive that construction just made, so its map init leaves the tank empty.</summary>
    private void OnConstructed(ConstructionChangeEntityEvent args)
    {
        if (HasComp<FTLDriveComponent>(args.New))
            _built.Add(args.New);
    }

    private void OnDriveInteractUsing(Entity<WFJumpFuelComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !IsFuel(args.Used, ent.Comp))
            return;

        args.Handled = true;
        Load(ent, args.Used, args.User, ent);
    }

    private void OnIntakeInteractUsing(Entity<WFJumpFuelIntakeComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || Transform(ent).GridUid is not { } grid || Tank(grid) is not { } tank || !IsFuel(args.Used, tank.Comp))
            return;

        args.Handled = true;
        Load(tank, args.Used, args.User, ent);
    }

    private bool IsFuel(EntityUid used, WFJumpFuelComponent tank)
    {
        return MetaData(used).EntityPrototype?.ID is { } id && id == tank.Good.Id;
    }

    /// <summary>Empties a crate into a tank, if a whole crate fits.</summary>
    private void Load(Entity<WFJumpFuelComponent> tank, EntityUid crate, EntityUid user, EntityUid at)
    {
        if (tank.Comp.Fuel + tank.Comp.PerCrate > tank.Comp.Capacity)
        {
            _popup.PopupEntity(Loc.GetString("wf-jump-fuel-full"), at, user);
            return;
        }

        tank.Comp.Fuel += tank.Comp.PerCrate;
        QueueDel(crate);
        _popup.PopupEntity(Loc.GetString("wf-jump-fuel-loaded",
            ("fuel", (int) tank.Comp.Fuel), ("capacity", (int) tank.Comp.Capacity)), at, user);
    }

    private void OnExamined(Entity<WFJumpFuelComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-jump-fuel-examine",
            ("fuel", (int) ent.Comp.Fuel), ("capacity", (int) ent.Comp.Capacity), ("crate", (int) ent.Comp.PerCrate)));
    }

    private void OnIntakeExamined(Entity<WFJumpFuelIntakeComponent> ent, ref ExaminedEvent args)
    {
        if (Transform(ent).GridUid is { } grid && Tank(grid) is { } tank)
            OnExamined(tank, ref args);
        else
            args.PushMarkup(Loc.GetString("wf-jump-fuel-intake-no-drive"));
    }

    /// <summary>The fuel tank of the drive a ship would jump with.</summary>
    public Entity<WFJumpFuelComponent>? Tank(EntityUid ship)
    {
        return _shuttle.TryGetFTLDrive(ship, out var drive, out _) && TryComp<WFJumpFuelComponent>(drive, out var tank)
            ? (drive.Value, tank)
            : null;
    }

    /// <summary>The jump fuel in the drive a ship would jump with, and its capacity; zero for no drive.</summary>
    public (float Fuel, float Capacity) Reserve(EntityUid ship)
    {
        return Tank(ship) is { } tank ? (tank.Comp.Fuel, tank.Comp.Capacity) : (0f, 0f);
    }

    /// <summary>Takes fuel from the drive a ship jumps with, never below empty.</summary>
    public void Spend(EntityUid ship, float amount)
    {
        if (Tank(ship) is { } tank)
            tank.Comp.Fuel = MathF.Max(0f, tank.Comp.Fuel - amount);
    }

    /// <summary>Sets a drive's fuel, clamped to its tank, for admins, NPC ships and tests.</summary>
    public void SetFuel(EntityUid ship, float amount)
    {
        if (Tank(ship) is { } tank)
            tank.Comp.Fuel = Math.Clamp(amount, 0f, tank.Comp.Capacity);
    }

    /// <summary>A station grid docked to the ship whose vendors sell the tank's fuel, with the price of a crate.</summary>
    private (EntityUid Station, int Price)? FuelStation(EntityUid ship, WFJumpFuelComponent tank)
    {
        foreach (var dock in _docking.GetDocks(ship))
        {
            if (!dock.Comp.Docked || dock.Comp.DockedWith is not { } other || Transform(other).GridUid is not { } grid || grid == ship)
                continue;

            if (_market.StationBuyQuotes(grid).TryGetValue(tank.Good, out var quote))
                return (grid, quote.Amount);
        }

        return null;
    }

    /// <summary>What filling the tank at a docked station would give and cost now, or null with no station.</summary>
    public (EntityUid Station, int Units, int Cost)? ServiceQuote(EntityUid ship)
    {
        if (Tank(ship) is not { } tank || FuelStation(ship, tank.Comp) is not { } station)
            return null;

        var stock = _market.Row(station.Station, tank.Comp.Good).Stock;
        var units = (int) MathF.Floor(MathF.Min(tank.Comp.Capacity - tank.Comp.Fuel, stock * tank.Comp.PerCrate));
        var cost = (int) MathF.Ceiling(MathF.Max(0, units) / tank.Comp.PerCrate * station.Price);
        return (station.Station, Math.Max(0, units), cost);
    }

    /// <summary>Fills a ship's tank at its docked station, charging the buyer; the refusal says why not.</summary>
    public bool TryBuy(EntityUid ship, EntityUid buyer, out string? refusal)
    {
        refusal = null;
        if (Tank(ship) is not { } tank || ServiceQuote(ship) is not { } quote)
        {
            refusal = "wf-jump-fuel-service-none";
            return false;
        }

        if (quote.Units <= 0)
        {
            refusal = tank.Comp.Fuel >= tank.Comp.Capacity ? "wf-jump-fuel-full" : "wf-jump-fuel-service-dry";
            return false;
        }

        if (!_bank.TryBankWithdraw(buyer, quote.Cost))
        {
            refusal = "wf-jump-fuel-service-funds";
            return false;
        }

        _market.AddStock(quote.Station, tank.Comp.Good, -quote.Units / tank.Comp.PerCrate);
        tank.Comp.Fuel = MathF.Min(tank.Comp.Capacity, tank.Comp.Fuel + quote.Units);
        UpdateService(ship);
        return true;
    }

    /// <summary>Gives every ship docked to a fuel-selling station the helm's refuel offer, and takes it off the rest.</summary>
    private void UpdateServices()
    {
        var offered = new HashSet<EntityUid>();
        var drives = EntityQueryEnumerator<WFJumpFuelComponent, TransformComponent>();
        while (drives.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is { } ship && HasComp<ShuttleComponent>(ship) && offered.Add(ship))
                UpdateService(ship);
        }

        var stale = new List<EntityUid>();
        var services = EntityQueryEnumerator<WFFuelServiceComponent>();
        while (services.MoveNext(out var uid, out _))
        {
            if (!offered.Contains(uid))
                stale.Add(uid);
        }

        foreach (var uid in stale)
        {
            RemComp<WFFuelServiceComponent>(uid);
        }
    }

    private void UpdateService(EntityUid ship)
    {
        if (ServiceQuote(ship) is not { } quote)
        {
            RemComp<WFFuelServiceComponent>(ship);
            return;
        }

        var (fuel, capacity) = Reserve(ship);
        var service = EnsureComp<WFFuelServiceComponent>(ship);
        var station = Name(quote.Station);
        if (service.Station == station && service.Units == quote.Units && service.Cost == quote.Cost
            && MathF.Abs(service.Fuel - MathF.Floor(fuel)) < 0.01f && MathF.Abs(service.Capacity - capacity) < 0.01f)
            return;

        service.Station = station;
        service.Units = quote.Units;
        service.Cost = quote.Cost;
        service.Fuel = MathF.Floor(fuel);
        service.Capacity = capacity;
        Dirty(ship, service);
    }
}
