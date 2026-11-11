#nullable enable
using System.Threading.Tasks;
using Content.Server._NF.Bank;
using Content.Server._WF.ScratchTickets;
using Content.Server._WF.Traders;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.ScratchTickets;
using Content.Shared._WF.Traders;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ScratchTickets;

/// <summary>
/// A spawned ticket is rolled, and an ATM pays a scratched winner into the bank and nothing else.
/// </summary>
[TestFixture]
[TestOf(typeof(WFScratchTicketSystem))]
public sealed class WFScratchTicketTest
{
    private const string TicketProto = "WFScratchTicket";
    private const int Balance = 3000;

    [Test]
    public async Task SpawnedTicketsAreRolled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                var ticket = entMan.GetComponent<WFScratchTicketComponent>(entMan.SpawnEntity(TicketProto, map.GridCoords));
                var winner = WFScratchTicketRules.Winner(ticket.Cells);

                Assert.Multiple(() =>
                {
                    Assert.That(ticket.Cells, Has.Count.EqualTo(WFScratchTicketRules.CellCount));
                    Assert.That(ticket.Scratched, Is.False);
                    Assert.That(ticket.Prize, Is.EqualTo(winner < 0 ? 0 : ticket.Symbols[winner].Prize));
                });
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SellerSellsTicketsAtTheirVendPrice()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var seller = EntityUid.Invalid;

        await server.WaitPost(() => seller = entMan.SpawnEntity("WFScratchTicketSeller", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var stock = entMan.System<TraderShopSystem>().GetStock((seller, entMan.GetComponent<TraderShopComponent>(seller)));
            Assert.That(stock, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(stock[0].Item, Is.EqualTo(TicketProto));
                Assert.That(stock[0].Price, Is.EqualTo(500));
                Assert.That(entMan.GetComponent<HumanoidAppearanceComponent>(seller).Species.Id, Is.EqualTo("Reptilian"));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AtmPaysOnlyAScratchedWinner()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var player = pair.Player!;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var tickets = entMan.System<WFScratchTicketSystem>();

        var atm = EntityUid.Invalid;
        var body = EntityUid.Invalid;
        var loser = EntityUid.Invalid;
        var winner = EntityUid.Invalid;
        Task setBank = Task.CompletedTask;

        await server.WaitPost(() =>
        {
            var mindSys = entMan.System<SharedMindSystem>();
            atm = entMan.SpawnEntity("ComputerBankATM", map.GridCoords);
            entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(atm, false);
            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(player.UserId).Owner, body);
            entMan.EnsureComponent<BankAccountComponent>(body);

            var current = prefs.GetPreferences(player.UserId);
            var profile = (HumanoidCharacterProfile) current.SelectedCharacter;
            setBank = prefs.SetProfile(player.UserId, current.SelectedCharacterIndex, profile.WithBankBalance(Balance));
            entMan.System<BankSystem>().RefreshBalance(body);

            loser = entMan.SpawnEntity(TicketProto, map.GridCoords);
            entMan.GetComponent<WFScratchTicketComponent>(loser).Prize = 0;
            winner = entMan.SpawnEntity(TicketProto, map.GridCoords);
            entMan.GetComponent<WFScratchTicketComponent>(winner).Prize = 1000;
        });
        await setBank;
        await pair.RunTicksSync(5);

        void Use(EntityUid ticket)
        {
            entMan.EventBus.RaiseLocalEvent(atm, new InteractUsingEvent(body, ticket, atm, map.GridCoords));
        }

        int Bank()
        {
            return ((HumanoidCharacterProfile) prefs.GetPreferences(player.UserId).SelectedCharacter).BankBalance;
        }

        await server.WaitPost(() => Use(winner));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(winner), Is.False, "The ATM took a ticket with its foil on.");
            Assert.That(Bank(), Is.EqualTo(Balance), "An unscratched ticket paid out.");
        });

        await server.WaitPost(() =>
        {
            tickets.Reveal((loser, entMan.GetComponent<WFScratchTicketComponent>(loser)), body);
            tickets.Reveal((winner, entMan.GetComponent<WFScratchTicketComponent>(winner)), body);
            Use(loser);
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(loser), Is.False, "The ATM took a losing ticket.");
            Assert.That(Bank(), Is.EqualTo(Balance), "A losing ticket paid out.");
        });

        await server.WaitPost(() => Use(winner));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(winner), "The ATM kept the winning ticket.");
            Assert.That(Bank(), Is.EqualTo(Balance + 1000), "The prize didn't reach the bank.");
            Assert.That(entMan.GetComponent<BankAccountComponent>(body).Balance, Is.EqualTo(Balance + 1000));
        });

        await pair.CleanReturnAsync();
    }
}
