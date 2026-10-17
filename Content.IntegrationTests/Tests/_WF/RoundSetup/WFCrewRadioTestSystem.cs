using System.Collections.Generic;
using Content.Server.Radio;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>Records delivered radio events after the real channel and membership filters.</summary>
public sealed class WFCrewRadioTestSystem : EntitySystem
{
    public readonly List<(EntityUid Receiver, string Text)> Deliveries = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewRadioTestReceiverComponent, RadioReceiveEvent>(OnReceive);
    }

    private void OnReceive(EntityUid uid, WFCrewRadioTestReceiverComponent component, ref RadioReceiveEvent args)
    {
        if (args.Channel.ID == "WFCrew")
            Deliveries.Add((uid, args.OriginalChatMsg.WrappedMessage));
    }
}

/// <summary>Observes test recipients without changing their radio eligibility.</summary>
[RegisterComponent]
public sealed partial class WFCrewRadioTestReceiverComponent : Component;
