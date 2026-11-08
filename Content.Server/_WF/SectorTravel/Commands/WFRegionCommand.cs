using System.Linq;
using Content.Server._WF.SectorControl.Systems;
using Content.Server._WF.SectorTravel.Components;
using Content.Server.Administration;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.SectorTravel;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SectorTravel.Commands;

/// <summary>The region from the console: list the sectors and beacons, move a ship between sectors, hold or raid a beacon.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFRegionCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WFSectorTravelSystem _sectors = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    private static readonly string[] Subcommands = { "status", "jump", "hold", "raid" };

    /// <summary>The spool an admin move takes, in seconds.</summary>
    private const float AdminSpool = 5f;

    public override string Command => "wf_region";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length > 0 ? args[0] : "status")
        {
            case "status":
                Status(shell);
                break;
            case "jump" when args.Length == 3:
                Jump(shell, args[1], args[2]);
                break;
            case "hold" when args.Length == 4:
                Hold(shell, args[1], args[2], args[3]);
                break;
            case "raid" when args.Length == 3:
                if (_sectors.FindBeacon(args[1], args[2]) is not { } raided)
                    shell.WriteError(Loc.GetString("wf-sector-travel-no-beacon"));
                else
                    shell.WriteLine(Loc.GetString(_sectors.TryRaid(raided) ? "cmd-wf_region-raided" : "cmd-wf_region-raid-failed"));
                break;
            default:
                shell.WriteError(Loc.GetString("cmd-wf_region-help", ("command", Command)));
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        var sectors = _prototypes.EnumeratePrototypes<WFSectorPrototype>().Select(sector => sector.ID).Order();
        var factions = _prototypes.EnumeratePrototypes<WFSectorFactionPrototype>().Select(faction => faction.ID).Order().Append("free");
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wf_region-hint-sub")),
            2 when args[0] == "jump" => CompletionResult.FromHint(Loc.GetString("cmd-wf_region-hint-grid")),
            3 when args[0] == "jump" => CompletionResult.FromHintOptions(sectors, Loc.GetString("cmd-wf_region-hint-sector")),
            2 or 3 when args[0] is "hold" or "raid" => CompletionResult.FromHintOptions(sectors, Loc.GetString("cmd-wf_region-hint-sector")),
            4 when args[0] == "hold" => CompletionResult.FromHintOptions(factions, Loc.GetString("cmd-wf_region-hint-faction")),
            _ => CompletionResult.Empty,
        };
    }

    private void Status(IConsoleShell shell)
    {
        foreach (var sector in _prototypes.EnumeratePrototypes<WFSectorPrototype>().OrderBy(sector => sector.ID))
        {
            var map = _sectors.MapOf(sector.ID) is { } uid ? EntityManager.GetComponent<TransformComponent>(uid).MapID.ToString() : "-";
            shell.WriteLine(Loc.GetString("cmd-wf_region-sector", ("sector", sector.ID), ("map", map),
                ("cut", _sectors.IsCutOff(sector.ID) ? 1 : 0)));
        }

        var query = EntityManager.EntityQueryEnumerator<WFJumpBeaconComponent>();
        while (query.MoveNext(out var uid, out var beacon))
        {
            var (_, cell) = _sectors.BeaconCell(uid);
            shell.WriteLine(Loc.GetString("cmd-wf_region-beacon", ("sector", beacon.Sector.Id), ("link", beacon.Link.Id),
                ("callsign", WFSectorHex.Callsign(cell)), ("holder", _sectors.Holder(uid)?.Id ?? "-"),
                ("closed", _sectors.Closes(uid) ? 1 : 0)));
        }
    }

    private void Jump(IConsoleShell shell, string gridArg, string sector)
    {
        if (!TryGrid(shell, gridArg, out var grid))
            return;

        if (_sectors.SectorOfEntity(grid) is not { } from || _sectors.FindBeacon(from, sector) is not { } beacon)
        {
            shell.WriteError(Loc.GetString("wf-sector-travel-no-link"));
            return;
        }

        if (!_sectors.TryTransit(grid, sector, AdminSpool, _sectors.TravelTime(beacon.Comp, from, sector), out var reason))
        {
            shell.WriteError(Loc.GetString(reason));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_region-jumping", ("sector", sector)));
    }

    private void Hold(IConsoleShell shell, string sector, string link, string faction)
    {
        if (_sectors.FindBeacon(sector, link) is not { } beacon)
        {
            shell.WriteError(Loc.GetString("wf-sector-travel-no-beacon"));
            return;
        }

        var (map, cell) = _sectors.BeaconCell(beacon);
        var done = faction == "free"
            ? _territory.Free(map, cell)
            : _prototypes.HasIndex<WFSectorFactionPrototype>(faction) && _territory.TryClaim(map, cell, faction, null);
        shell.WriteLine(Loc.GetString(done ? "cmd-wf_region-held" : "cmd-wf_region-hold-failed",
            ("callsign", WFSectorHex.Callsign(cell)), ("faction", faction)));
        _sectors.RefreshRegion();
    }

    private bool TryGrid(IConsoleShell shell, string arg, out EntityUid grid)
    {
        grid = default;
        if (!NetEntity.TryParse(arg, out var net) || !EntityManager.TryGetEntity(net, out var uid)
            || EntityManager.GetComponent<TransformComponent>(uid.Value).GridUid is not { } found)
        {
            shell.WriteError(Loc.GetString("cmd-wf_region-bad-grid"));
            return false;
        }

        grid = found;
        return true;
    }
}
