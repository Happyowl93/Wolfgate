using System.Globalization;
using Content.Server._WF.Companies;
using Content.Server.Radio.EntitySystems;
using Content.Shared._WF.Roles;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Roles;

/// <summary>Sends the arrival line for <see cref="WFRadioOnJoinComponent"/>, naming the same title the station announcement would.</summary>
public sealed partial class WFRadioOnJoinSystem : EntitySystem
{
    [Dependency] private WFCompanySystem _company = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private RadioSystem _radio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFRadioOnJoinComponent, PlayerSpawnCompleteEvent>(OnSpawned);
    }

    private void OnSpawned(EntityUid uid, WFRadioOnJoinComponent comp, PlayerSpawnCompleteEvent args)
    {
        if (args.Silent)
            return;

        var title = _company.RankTitleFor(args.Player, args.Profile, args.JobId)
            ?? CustomJobTitleRules.GetTitle(args.Profile, args.JobId, _prototype)
            ?? JobName(args.JobId);

        var message = Loc.GetString("wf-radio-on-join", ("character", Name(uid)), ("job", title));
        _radio.SendRadioMessage(args.Station, message, comp.RadioChannel, uid);
    }

    private string JobName(string? job)
    {
        return job != null && _prototype.TryIndex<JobPrototype>(job, out var proto)
            ? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(proto.LocalizedName)
            : Loc.GetString("wf-radio-on-join-no-job");
    }
}
