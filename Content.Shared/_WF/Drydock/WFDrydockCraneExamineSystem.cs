using Content.Shared.Examine;
using Robust.Shared.Localization;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// Tells who examines a drydock crane which console it is linked to and how much of its power it receives.
/// </summary>
public sealed class WFDrydockCraneExamineSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFDrydockCraneComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, WFDrydockCraneComponent comp, ExaminedEvent args)
    {
        args.PushMarkup(comp.LinkedConsole is { } console
            ? Loc.GetString("drydock-crane-examine-linked", ("console", MetaData(console).EntityName))
            : Loc.GetString("drydock-crane-examine-unlinked"));
        if (comp.Blocked)
            args.PushMarkup(Loc.GetString("drydock-crane-examine-blocked", ("blocker", comp.Blocker ?? string.Empty)));

        args.PushMarkup(Loc.GetString("drydock-crane-examine-power", ("percent", (int) MathF.Round(comp.PowerRatio * 100f))));
    }
}
