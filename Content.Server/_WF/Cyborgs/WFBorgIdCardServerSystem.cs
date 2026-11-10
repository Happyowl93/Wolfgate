using Content.Server._NF.SectorServices;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared._WF.Cyborgs;
using Content.Shared.Access.Systems;
using Content.Shared.Hands;
using Content.Shared.Roles;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.StationRecords;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Cyborgs;

/// <summary>
/// Gives a cyborg's issued ID card a crew record, the key ship doors, allow lists and crews admit a card by.
/// </summary>
public sealed partial class WFBorgIdCardServerSystem : EntitySystem
{
    /// <summary>The job a cyborg's record is filed under.</summary>
    private static readonly ProtoId<JobPrototype> RecordJob = "Borg";

    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private StationRecordKeyStorageSystem _keyStorage = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private WFBorgIdCardSystem _cards = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgChassisComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<WFBorgIdCardComponent, GotEquippedHandEvent>(OnCardEquipped);
    }

    private void OnPlayerAttached(Entity<BorgChassisComponent> ent, ref PlayerAttachedEvent args)
    {
        if (_cards.TryGetCard(ent, out var card))
            EnsureRecord(ent, card);
    }

    // Covers a tool module fitted after the player took the chassis.
    private void OnCardEquipped(Entity<WFBorgIdCardComponent> ent, ref GotEquippedHandEvent args)
    {
        if (HasComp<BorgChassisComponent>(args.User))
            EnsureRecord(args.User, ent);
    }

    /// <summary>
    /// Files the cyborg on the sector records service, as a crew spawn is, and keys a card that has no record yet
    /// to it.
    /// </summary>
    private void EnsureRecord(EntityUid borg, EntityUid card)
    {
        if (!TryComp<StationRecordKeyStorageComponent>(card, out var storage) || storage.Key != null)
            return;

        var service = _sectorService.GetServiceEntity();
        if (!TryComp<StationRecordsComponent>(service, out var records) || !_proto.TryIndex(RecordJob, out var job))
            return;

        var name = Name(borg);
        // Always a record of its own: one shared by name would let a namesake's card open the same doors.
        var key = _records.AddRecordEntry(service, new GeneralStationRecord
        {
            Name = name,
            JobTitle = job.LocalizedName,
            JobIcon = job.Icon,
            JobPrototype = job.ID,
            DisplayPriority = job.RealDisplayWeight,
        }, records);

        if (!key.IsValid())
            return;

        _keyStorage.AssignKey(card, key, storage);
        _idCard.TryChangeFullName(card, name);
    }
}
