#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._NF.Bank;
using Content.Server._WF.Banking;
using Content.Server.CartridgeLoader;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Banking;
using Content.Shared.CartridgeLoader;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Banking;

/// <summary>
/// The Bank app signs into the account of an ID card, pays withdrawals out as cash, holds every account to the round's
/// allowance, takes deposits from the hand, and ships on every PDA.
/// </summary>
[TestOf(typeof(WFBankSystem))]
public sealed class WFBankTest : InteractionTest
{
    [Test]
    public async Task WithdrawWithinAllowanceTest()
    {
        var banking = SEntMan.System<WFBankSystem>();
        var bank = SEntMan.System<BankSystem>();
        var hands = SEntMan.System<SharedHandsSystem>();
        var prefs = Server.ResolveDependency<IServerPreferencesManager>();
        var user = ServerSession.UserId;

        // 3,000,000 opens the round, so the allowance is 10% of it: 300,000.
        var save = Task.CompletedTask;
        var slot = 0;
        var name = string.Empty;
        await Server.WaitPost(() =>
        {
            var p = prefs.GetPreferences(user);
            var profile = (HumanoidCharacterProfile) p.SelectedCharacter;
            slot = p.SelectedCharacterIndex;
            name = profile.Name;
            save = prefs.SetProfile(user, slot, profile.WithBankBalance(3_000_000));
            SEntMan.EnsureComponent<BankAccountComponent>(SPlayer);
        });
        await PoolManager.WaitUntil(Server, () => save.IsCompleted, maxTicks: 300);

        EntityUid card = default;
        EntityUid stranger = default;
        await Server.WaitPost(() =>
        {
            card = SEntMan.SpawnEntity("PassengerIDCard", SEntMan.GetCoordinates(PlayerCoords));
            stranger = SEntMan.SpawnEntity("PassengerIDCard", SEntMan.GetCoordinates(PlayerCoords));
            banking.Link(card, user, slot, name, 3_000_000);
        });

        int Balance()
        {
            bank.TryGetBalance(ServerSession, out var balance);
            return balance;
        }

        int HeldCash()
        {
            return hands.EnumerateHeld(SPlayer)
                .Select(held => SEntMan.TryGetComponent<StackComponent>(held, out var stack) && stack.StackTypeId == "Credit" ? stack.Count : 0)
                .Sum();
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(banking.State(SPlayer, null).Status, Is.EqualTo(WFBankStatus.NoId));
            Assert.That(banking.State(SPlayer, stranger).Status, Is.EqualTo(WFBankStatus.Unlinked));

            var state = banking.State(SPlayer, card);
            Assert.That(state.Status, Is.EqualTo(WFBankStatus.Ready));
            Assert.That(state.Holder, Is.EqualTo(name));
            Assert.That(state.Balance, Is.EqualTo(3_000_000));
            Assert.That(state.Limit, Is.EqualTo(300_000));

            Assert.That(banking.Withdraw(SPlayer, card, 200_000), Is.Null);
            Assert.That(Balance(), Is.EqualTo(2_800_000));
            Assert.That(HeldCash(), Is.EqualTo(200_000), "The withdrawal is paid out as cash in hand.");

            Assert.That(banking.Transfer(SPlayer, card, SPlayer, 1_000), Is.Not.Null, "An account can't transfer to its own holder.");
            Assert.That(banking.Transfer(SPlayer, card, "No Such Spacer", 1_000), Is.Not.Null, "Transfers need a connected account holder's exact name.");
            Assert.That(banking.Withdraw(SPlayer, card, 150_000), Is.Not.Null, "Only 100,000 of the allowance is left.");
            Assert.That(banking.Withdraw(SPlayer, card, 0), Is.Not.Null);
            Assert.That(Balance(), Is.EqualTo(2_800_000), "Refused withdrawals take nothing.");

            Assert.That(banking.Withdraw(SPlayer, card, 100_000), Is.Null);
            Assert.That(Balance(), Is.EqualTo(2_700_000));
            Assert.That(banking.Withdraw(SPlayer, card, 1), Is.Not.Null, "The allowance is spent for the round.");

            state = banking.State(SPlayer, card);
            Assert.That(state.Withdrawn, Is.EqualTo(300_000));
            Assert.That(state.History.Count(entry => entry.Kind == WFBankEntryKind.Withdrawal), Is.EqualTo(2));
            Assert.That(state.History, Has.Count.EqualTo(2), "The app's own withdrawals aren't noted twice.");

            // Money moved outside the app, like a fine or wages, shows in the activity but leaves the allowance alone.
            Assert.That(bank.TryBankWithdraw(SPlayer, 5_000), Is.True);
            Assert.That(bank.TryBankDeposit(SPlayer, 7_000, tax: false), Is.True);
            state = banking.State(SPlayer, card);
            Assert.That(state.History[0].Kind, Is.EqualTo(WFBankEntryKind.Payment));
            Assert.That(state.History[0].Amount, Is.EqualTo(7_000));
            Assert.That(state.History[1].Kind, Is.EqualTo(WFBankEntryKind.Charge));
            Assert.That(state.History[1].Amount, Is.EqualTo(5_000));
            Assert.That(state.Withdrawn, Is.EqualTo(300_000));
        });

        // Depositing the cash in the active hand puts it back, though the allowance stays spent.
        await Server.WaitAssertion(() =>
        {
            var before = Balance();
            var held = hands.GetActiveItem(SPlayer);
            Assert.That(held, Is.Not.Null, "A withdrawal lands in the active hand first.");
            Assert.That(banking.Deposit(SPlayer, card, out var deposited), Is.Null);
            Assert.That(deposited, Is.GreaterThan(0));
            Assert.That(SEntMan.Deleted(held), Is.True, "The deposited cash is taken.");
            Assert.That(Balance(), Is.GreaterThan(before));
            Assert.That(Balance(), Is.LessThanOrEqualTo(before + deposited));
            var state = banking.State(SPlayer, card);
            Assert.That(state.Withdrawn, Is.EqualTo(300_000));
            Assert.That(state.History[0].Kind, Is.EqualTo(WFBankEntryKind.Deposit));
            Assert.That(state.History[1].Kind, Is.EqualTo(WFBankEntryKind.Payment), "The app's deposit isn't noted twice.");
        });
    }

    [Test]
    public async Task EveryPdaHasTheBankAppTest()
    {
        var factory = Server.ResolveDependency<IComponentFactory>();
        var loaders = SEntMan.System<CartridgeLoaderSystem>();

        await Server.WaitAssertion(() =>
        {
            var missing = new List<string>();
            foreach (var proto in ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract
                    || !proto.TryComp<PdaComponent>(out _, factory)
                    || !proto.TryComp<CartridgeLoaderComponent>(out var loader, factory)
                    || loader.PreinstalledPrograms.Count >= loader.DiskSpace)
                    continue;

                var pda = SEntMan.SpawnEntity(proto.ID, SEntMan.GetCoordinates(PlayerCoords));
                var installed = loaders.GetInstalled(pda)
                    .Select(program => SEntMan.GetComponent<MetaDataComponent>(program).EntityPrototype?.ID);
                if (!installed.Contains(WFBankSystem.BankCartridge.Id))
                    missing.Add(proto.ID);

                SEntMan.DeleteEntity(pda);
            }

            Assert.That(missing, Is.Empty, "PDAs without the Bank app: " + string.Join(", ", missing));
        });
    }
}
