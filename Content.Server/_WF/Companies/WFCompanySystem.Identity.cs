using System.Linq;
using Content.Server._NF.Station.Components;
using Content.Server._WF.RoundSetup;
using Content.Server.Administration.Managers;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Radio;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared._WF.Companies;
using Content.Shared._WF.Roles;
using Content.Shared.Access.Systems;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Radio.Components;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Companies;

public sealed partial class WFCompanySystem
{
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IBanManager _bans = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private StationJobsSystem _stationJobs = default!;
    [Dependency] private SharedIdCardSystem _idCards = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private WFRoundSetupSystem _roundSetup = default!;

    /// <summary>The shared input channel, routed to a separate group for each company.</summary>
    public const string CompanyChannel = "WFCompany";

    /// <summary>Rank titles waiting for a member who chose Join as rank to finish spawning.</summary>
    private readonly Dictionary<NetUserId, string> _rankJoins = new();
    /// <summary>Company id by the body of each member playing their member character.</summary>
    private readonly Dictionary<EntityUid, int> _companyBodies = new();
    private TimeSpan _nextRadioSync;

    private void InitializeIdentity()
    {
        SubscribeLocalEvent<RadioSendAttemptEvent>(OnRadioSend);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnRadioReceive);
        SubscribeLocalEvent<WFCompanyIdComponent, ExaminedEvent>(OnIdExamined);
        // After every other price handler, so nothing adds value back on top.
        SubscribeLocalEvent<WFCompanyPropertyComponent, PriceCalculationEvent>(OnPropertyPrice, after: new[]
        {
            typeof(PricingSystem), typeof(CargoSystem), typeof(Content.Server.Atmos.Piping.Unary.EntitySystems.GasCanisterSystem),
            typeof(Content.Server.Armor.ArmorSystem), typeof(Content.Server.Anomaly.Effects.AnomalyCoreSystem),
            typeof(Content.Server.Atmos.EntitySystems.GasTankSystem), typeof(Content.Server.Materials.MaterialStorageSystem),
            typeof(Content.Server.Nyanotrasen.Kitchen.EntitySystems.DeepFryerSystem), typeof(Content.Server.VendingMachines.VendingMachineSystem),
            typeof(Content.Server.Power.EntitySystems.BatterySystem), typeof(Content.Server.Weapons.Ranged.Systems.GunSystem),
            typeof(Content.Server.Xenoarchaeology.Artifact.XenoArtifactSystem), typeof(Content.Server._Mono.ScuttleDevice.ScuttleDeviceSystem),
            typeof(Content.Server._Mono.Cargo.DriftingPriceSystem), typeof(Content.Server._WF.Drydock.WFDrydockSystem),
        });
    }

    /// <summary>Marks the member's ID with their company and applies a Join as rank title.</summary>
    private void ApplyMemberIdentity(ICommonSession session, EntityUid body, int slot)
    {
        var title = _rankJoins.Remove(session.UserId, out var joined) ? joined : null;
        if (!_profileIds.TryGetValue(session.UserId, out var ids) || !ids.TryGetValue(slot, out var profile) ||
            CompanyOf(profile) is not { } company)
            return;
        var member = company.Members[profile];
        var rank = TitleOf(company, member);
        if (!_idCards.TryFindIdCard(body, out var card))
            return;
        CaptureMemberKey(profile, card);
        var marker = EnsureComp<WFCompanyIdComponent>(card);
        marker.Company = company.Name;
        marker.Tag = company.Tag;
        marker.Rank = rank;
        if (IconRankOf(company, member) is { } iconRank)
        {
            var icon = EnsureComp<WFCompanyRankIconComponent>(card);
            icon.RankId = iconRank.Id;
            Dirty(card, icon);
        }
        if (title == null)
            return;
        if (_minds.TryGetMind(body, out var mind, out _) && _prototypes.TryIndex<JobPrototype>(BaseJob(company, member), out _))
        {
            var custom = EnsureComp<CustomJobTitleComponent>(mind);
            custom.Job = BaseJob(company, member);
            custom.Title = title;
        }
        _idCards.TryChangeJobTitle(card, title);
        if (TryComp<StationRecordKeyStorageComponent>(card, out var storage) && storage.Key is { } key &&
            _records.TryGetRecord<GeneralStationRecord>(key, out var record))
        {
            record.JobTitle = title;
            _records.Synchronize(key);
        }
    }

    private void OnIdExamined(Entity<WFCompanyIdComponent> card, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("wf-company-id-examine", ("tag", card.Comp.Tag), ("company", card.Comp.Company), ("rank", card.Comp.Rank)));
    }

    /// <summary>The job a member spawns as: their rank's base job, or the owner rank's for the owner.</summary>
    private static string BaseJob(Company company, Member member) => IconRankOf(company, member)?.BaseJob ?? "Contractor";

    /// <summary>Late-joins a member as their rank: the base job's equipment and access, the rank title on ID and records.</summary>
    private string? JoinAsRank(ICommonSession session, Company company, Member member, Character character)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || _ticker.DisallowLateJoin || _ticker.UserHasJoinedGame(session))
            return "wf-company-error-join-closed";
        if (_preferences.GetPreferencesOrNull(session.UserId)?.SelectedCharacterIndex != character.Slot)
            return "wf-company-error-character";
        var job = BaseJob(company, member);
        var bans = _bans.GetJobBans(session.UserId);
        var allowed = new IsJobAllowedEvent(session, new ProtoId<JobPrototype>(job));
        RaiseLocalEvent(ref allowed);
        if (bans == null || bans.Contains(job) || allowed.Cancelled)
            return "wf-company-error-job";
        var query = EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent>();
        EntityUid? station = null;
        while (query.MoveNext(out var candidate, out _, out _))
        {
            if (_roundSetup.IsStandardStation(candidate) && !HasComp<ExtraShuttleInformationComponent>(candidate) &&
                _stationJobs.TryGetJobSlot(candidate, job, out var slots) && slots != 0)
            {
                station = candidate;
                break;
            }
        }
        if (station == null)
            return "wf-company-error-no-slot";
        _rankJoins[session.UserId] = TitleOf(company, member) is { Length: > 0 } joinTitle ? joinTitle : job;
        _ticker.MakeJoinGame(session, station.Value, job);
        if (_ticker.UserHasJoinedGame(session))
            return null;
        _rankJoins.Remove(session.UserId);
        return "wf-company-error-failed";
    }

    /// <summary>The body a session plays as its member character, alive or critical, AFK or not.</summary>
    private (EntityUid Body, Company Company)? MemberBody(ICommonSession session)
    {
        if (session.Status != SessionStatus.InGame || !_spawnedSlots.TryGetValue(session.UserId, out var slot) ||
            session.AttachedEntity is not { } body || HasComp<GhostComponent>(body) ||
            !TryComp<MobStateComponent>(body, out var state) || state.CurrentState == MobState.Dead ||
            !_profileIds.TryGetValue(session.UserId, out var ids) || !ids.TryGetValue(slot, out var profile) ||
            CompanyOf(profile) is not { } company)
            return null;
        return (body, company);
    }

    /// <summary>Gives each playing member the company channel and takes it from everyone else.</summary>
    private void SyncCompanyRadios()
    {
        if (_timing.CurTime < _nextRadioSync)
            return;
        _nextRadioSync = _timing.CurTime + TimeSpan.FromSeconds(1);
        var bodies = new Dictionary<EntityUid, int>();
        foreach (var session in _players.Sessions)
        {
            if (MemberBody(session) is not { } found)
                continue;
            bodies[found.Body] = found.Company.Id;
            InstallCompanyRadio(found.Body);
        }
        foreach (var body in _companyBodies.Keys.Where(body => !bodies.ContainsKey(body)).ToArray())
            RemoveCompanyRadio(body);
        _companyBodies.Clear();
        foreach (var (body, company) in bodies)
            _companyBodies[body] = company;
    }

    private int? CompanyForBody(EntityUid body) =>
        !TerminatingOrDeleted(body) && _companyBodies.TryGetValue(body, out var company) && !HasComp<GhostComponent>(body) &&
        (!TryComp<MobStateComponent>(body, out var state) || state.CurrentState != MobState.Dead) ? company : null;

    private void OnRadioSend(ref RadioSendAttemptEvent args)
    {
        if (args.Channel.ID == CompanyChannel && CompanyForBody(args.RadioSource) == null)
            args.Cancelled = true;
    }

    private void OnRadioReceive(ref RadioReceiveAttemptEvent args)
    {
        if (args.Channel.ID == CompanyChannel && (CompanyForBody(args.RadioSource) is not { } company ||
            CompanyForBody(args.RadioReceiver) != company))
            args.Cancelled = true;
    }

    /// <summary>Names the speaker's company channel.</summary>
    public string CompanyRadioName(EntityUid source) =>
        CompanyForBody(source) is { } id && _companies.TryGetValue(id, out var company)
            ? Loc.GetString("wf-company-radio-channel", ("tag", company.Tag))
            : Loc.GetString("wf-company-radio");

    private void InstallCompanyRadio(EntityUid body)
    {
        var owned = EnsureComp<WFCompanyRadioComponent>(body);
        if (!TryComp<IntrinsicRadioTransmitterComponent>(body, out var transmitter))
        {
            transmitter = AddComp<IntrinsicRadioTransmitterComponent>(body);
            transmitter.Channels.Clear();
            owned.AddedTransmitter = true;
        }
        transmitter.Channels.Add(CompanyChannel);
        if (!HasComp<IntrinsicRadioReceiverComponent>(body))
        {
            AddComp<IntrinsicRadioReceiverComponent>(body);
            owned.AddedReceiver = true;
        }
        if (!TryComp<ActiveRadioComponent>(body, out var active))
        {
            active = AddComp<ActiveRadioComponent>(body);
            owned.AddedActive = true;
        }
        active.Channels.Add(CompanyChannel);
    }

    private void RemoveCompanyRadio(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !TryComp<WFCompanyRadioComponent>(body, out var owned))
            return;
        if (TryComp<IntrinsicRadioTransmitterComponent>(body, out var transmitter))
        {
            transmitter.Channels.Remove(CompanyChannel);
            if (owned.AddedTransmitter && transmitter.Channels.Count == 0)
                RemComp<IntrinsicRadioTransmitterComponent>(body);
        }
        if (TryComp<ActiveRadioComponent>(body, out var active))
        {
            active.Channels.Remove(CompanyChannel);
            if (active.Channels.Count == 0 && !active.ReceiveAllChannels)
            {
                if (owned.AddedActive)
                    RemComp<ActiveRadioComponent>(body);
                if (owned.AddedReceiver)
                    RemComp<IntrinsicRadioReceiverComponent>(body);
            }
        }
        RemComp<WFCompanyRadioComponent>(body);
    }

    /// <summary>The company of a session's current character, or null.</summary>
    public int? CompanyIdOf(ICommonSession session) => CurrentCharacter(session) is { } character ? CompanyOf(character.ProfileId)?.Id : null;

    /// <summary>Whether the session's current character may run crews under this company.</summary>
    public bool CanFieldCrew(ICommonSession session, int companyId) =>
        CurrentCharacter(session) is { } character && CompanyOf(character.ProfileId) is { } company && company.Id == companyId &&
        Has(company, company.Members[character.ProfileId], WFCompanyPermissions.FieldCrew);

    /// <summary>The rank title and base job a session's current character joins as; the owner uses the top rank's job.</summary>
    public (string Title, string Job)? RankFor(ICommonSession session)
    {
        if (CurrentCharacter(session) is not { } character || CompanyOf(character.ProfileId) is not { } company)
            return null;
        var member = company.Members[character.ProfileId];
        return (TitleOf(company, member), BaseJob(company, member));
    }

    /// <summary>Adds the company on the target's ID to their examine card.</summary>
    public void FillExamineCard(EntityUid target, Content.Shared._WF.ExamineCard.ExamineCardEvent card)
    {
        if (!_idCards.TryFindIdCard(target, out var id) || !TryComp<WFCompanyIdComponent>(id, out var marker))
            return;
        card.Company = $"[{marker.Tag}] {marker.Company}";
        card.CompanyRank = marker.Rank;
        var company = _companies.Values.FirstOrDefault(entry => entry.Tag == marker.Tag);
        if (company == null)
            return;
        card.CompanyId = company.Id;
        card.CompanyLogoVersion = company.Logo == null ? 0 : company.LogoVersion;
        if (TryComp<WFCompanyRankIconComponent>(id, out var rankIcon) && FindRank(rankIcon.RankId) is { } rank)
        {
            card.CompanyRankId = rank.Id;
            card.CompanyIconVersion = rank.Icon == null ? 0 : rank.IconVersion;
        }
    }

    /// <summary>Public identity of a company, for crew rows and ship IFF.</summary>
    public (string Name, string Tag, Color Colour)? CompanyIdentity(int companyId) =>
        _companies.TryGetValue(companyId, out var company) ? (company.Name, company.Tag, company.Colour) : null;
}
