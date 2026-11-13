#nullable enable
using System.Linq;
using Content.Server._WF.NanoChat;
using Content.Server.CartridgeLoader;
using Content.Shared._WF.NanoChat;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.NanoChat;

/// <summary>Cards get distinct numbers, messages reach another PDA while a telecom server is up, and sending is rate limited.</summary>
[TestFixture]
[TestOf(typeof(NanoChatCartridgeSystem))]
public sealed class NanoChatTest
{
    private const string Pda = "PassengerPDA";
    private const string IdCard = "PassengerIDCard";
    private const string Server = "TelecomServerFilledCommon";

    [Test]
    public async Task CardsGetDistinctNumbers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cards = Enumerable.Range(0, 40).Select(_ => entMan.SpawnEntity(IdCard, map.GridCoords)).ToList();
            var numbers = cards.Select(c => entMan.GetComponent<NanoChatCardComponent>(c).Number).ToList();

            Assert.That(numbers, Has.All.Not.Null);
            Assert.That(numbers, Is.Unique);
            Assert.That(numbers.Select(n => n!.Value), Has.All.InRange(1u, 9999u));

            // Giving one card another's number moves the other onto a free one.
            var nanoChat = entMan.System<NanoChatSystem>();
            var first = (cards[0], entMan.GetComponent<NanoChatCardComponent>(cards[0]));
            var taken = numbers[1]!.Value;
            nanoChat.AssignNumber(first, taken);
            Assert.That(first.Item2.Number, Is.EqualTo(taken));
            Assert.That(entMan.GetComponent<NanoChatCardComponent>(cards[1]).Number, Is.Not.EqualTo(taken));
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MessageNeedsSignalAndIsRateLimited()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid telecom = default;
        EntityUid pdaA = default;
        EntityUid pdaB = default;
        await server.WaitPost(() =>
        {
            telecom = entMan.SpawnEntity(Server, map.GridCoords);
            entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(telecom, false);
            pdaA = entMan.SpawnEntity(Pda, map.GridCoords);
            pdaB = entMan.SpawnEntity(Pda, map.GridCoords);
        });

        // The power net updates twice a second.
        await pair.RunTicksSync(90);

        Entity<NanoChatCardComponent> cardA = default;
        Entity<NanoChatCardComponent> cardB = default;
        EntityUid cartA = default;
        await server.WaitAssertion(() =>
        {
            cardA = CardOf(entMan, pdaA);
            cardB = CardOf(entMan, pdaB);
            Assert.That(cardA.Comp.PdaUid, Is.EqualTo(pdaA));
            Assert.That(entMan.System<CartridgeLoaderSystem>()
                .TryGetProgram<NanoChatCartridgeComponent>(pdaA, out var program, out _), "PDA has no NanoChat app.");
            cartA = program!.Value;

            Send(entMan, pdaA, cartA, cardB.Comp.Number!.Value, "hello");
            Send(entMan, pdaA, cartA, cardB.Comp.Number!.Value, "too soon");

            var sent = cardA.Comp.Messages[cardB.Comp.Number!.Value];
            Assert.That(sent, Has.Count.EqualTo(1), "The second message got past the rate limit.");
            Assert.That(sent[0].DeliveryFailed, Is.False, "The message failed with a telecom server up.");

            Assert.That(cardB.Comp.Messages.TryGetValue(cardA.Comp.Number!.Value, out var received), "Nothing arrived.");
            Assert.That(received!.Select(m => m.Content), Is.EqualTo(new[] { "hello" }));
            Assert.That(cardB.Comp.Recipients[cardA.Comp.Number!.Value].HasUnread, Is.True);

            entMan.DeleteEntity(telecom);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Send(entMan, pdaA, cartA, cardB.Comp.Number!.Value, "anyone?");
            var sent = cardA.Comp.Messages[cardB.Comp.Number!.Value];
            Assert.That(sent, Has.Count.EqualTo(2));
            Assert.That(sent[1].DeliveryFailed, Is.True, "A message went out with no telecom server.");
            Assert.That(cardB.Comp.Messages[cardA.Comp.Number!.Value], Has.Count.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    private static Entity<NanoChatCardComponent> CardOf(IEntityManager entMan, EntityUid pda)
    {
        var id = entMan.GetComponent<PdaComponent>(pda).ContainedId;
        Assert.That(id, Is.Not.Null, "PDA spawned without an ID.");
        return (id!.Value, entMan.GetComponent<NanoChatCardComponent>(id.Value));
    }

    private static void Send(IEntityManager entMan, EntityUid pda, EntityUid cartridge, uint number, string text)
    {
        var msg = new NanoChatUiMessageEvent(NanoChatUiMessageType.SendMessage, number, text)
        {
            LoaderUid = entMan.GetNetEntity(pda),
            Actor = pda,
            User = pda,
        };
        entMan.EventBus.RaiseLocalEvent<CartridgeMessageEvent>(cartridge, msg);
    }
}
