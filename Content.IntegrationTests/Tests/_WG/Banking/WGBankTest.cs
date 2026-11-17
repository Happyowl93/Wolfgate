#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._NF.Bank;
using Content.Server._WG.Banking;
using Content.Server.CartridgeLoader;
using Content.Server.Preferences.Managers;
using Content.Server.Stack;
using Content.Shared._Mono.CCVar;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.Banking;
using Content.Shared.CartridgeLoader;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Banking;

/// <summary>
/// The Bank app signs into the account of an ID card, pays withdrawals out as cash, holds every account to the round's
/// allowance, takes deposits from the hand, and ships on every PDA.
/// </summary>
[TestOf(typeof(WGBankSystem))]
public sealed class WGBankTest : InteractionTest
{
    [Test]
    public async Task WithdrawWithinAllowanceTest()
    {
        var banking = SEntMan.System<WGBankSystem>();
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
            Assert.That(banking.State(SPlayer, null).Status, Is.EqualTo(WGBankStatus.NoId));
            Assert.That(banking.State(SPlayer, stranger).Status, Is.EqualTo(WGBankStatus.Unlinked));

            var state = banking.State(SPlayer, card);
            Assert.That(state.Status, Is.EqualTo(WGBankStatus.Ready));
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
            Assert.That(state.History.Count(entry => entry.Kind == WGBankEntryKind.Withdrawal), Is.EqualTo(2));
            Assert.That(state.History, Has.Count.EqualTo(2), "The app's own withdrawals aren't noted twice.");

            // Money moved outside the app, like a fine or wages, shows in the activity but leaves the allowance alone.
            Assert.That(bank.TryBankWithdraw(SPlayer, 5_000), Is.True);
            Assert.That(bank.TryBankDeposit(SPlayer, 7_000, tax: false), Is.True);
            state = banking.State(SPlayer, card);
            Assert.That(state.History[0].Kind, Is.EqualTo(WGBankEntryKind.Payment));
            Assert.That(state.History[0].Amount, Is.EqualTo(7_000));
            Assert.That(state.History[1].Kind, Is.EqualTo(WGBankEntryKind.Charge));
            Assert.That(state.History[1].Amount, Is.EqualTo(5_000));
            Assert.That(state.Withdrawn, Is.EqualTo(300_000));
        });

        // Depositing the cash in the active hand puts it back up to the round's deposit limit, and the allowance stays spent.
        await Server.WaitAssertion(() =>
        {
            var before = Balance();
            var cashBefore = HeldCash();
            var held = hands.GetActiveItem(SPlayer);
            Assert.That(held, Is.Not.Null, "A withdrawal lands in the active hand first.");
            Assert.That(SEntMan.GetComponent<StackComponent>(held!.Value).Count, Is.GreaterThan(50_000));
            Assert.That(banking.Deposit(SPlayer, card, out var deposited), Is.Null);
            Assert.That(deposited, Is.EqualTo(50_000), "A deposit takes no more than the round's limit.");
            Assert.That(SEntMan.Deleted(held), Is.False, "The cash over the limit stays in hand.");
            Assert.That(HeldCash(), Is.EqualTo(cashBefore - 50_000));
            Assert.That(Balance(), Is.GreaterThan(before));
            Assert.That(Balance(), Is.LessThanOrEqualTo(before + deposited));
            var state = banking.State(SPlayer, card);
            Assert.That(state.Withdrawn, Is.EqualTo(300_000));
            Assert.That(state.Deposited, Is.EqualTo(50_000));
            Assert.That(state.DepositLimit, Is.EqualTo(50_000));
            Assert.That(state.History[0].Kind, Is.EqualTo(WGBankEntryKind.Deposit));
            Assert.That(state.History[1].Kind, Is.EqualTo(WGBankEntryKind.Payment), "The app's deposit isn't noted twice.");

            var after = Balance();
            Assert.That(banking.Deposit(SPlayer, card, out _), Is.Not.Null, "The deposit limit is spent for the round.");
            Assert.That(Balance(), Is.EqualTo(after));
            Assert.That(HeldCash(), Is.EqualTo(cashBefore - 50_000), "A refused deposit takes nothing.");
        });
    }

    /// <summary>
    /// Away from the holder's body the app saves the profile itself: it never reaches a slot the holder isn't playing,
    /// since saving would select it, it locks an Ironman character, and it takes the deposit tax.
    /// </summary>
    [Test]
    public async Task ProfileAccountTest()
    {
        var banking = SEntMan.System<WGBankSystem>();
        var bank = SEntMan.System<BankSystem>();
        var hands = SEntMan.System<SharedHandsSystem>();
        var stacks = SEntMan.System<StackSystem>();
        var prefs = Server.ResolveDependency<IServerPreferencesManager>();
        var user = ServerSession.UserId;

        var slot = 0;
        var other = 0;
        HumanoidCharacterProfile profile = default!;
        var save = Task.CompletedTask;
        await Server.WaitPost(() =>
        {
            var p = prefs.GetPreferences(user);
            slot = p.SelectedCharacterIndex;
            other = slot == 0 ? 1 : 0;
            profile = ((HumanoidCharacterProfile) p.SelectedCharacter).WithBankBalance(3_000_000);
            // Saving a slot selects it, so the played character is saved last.
            save = Task.WhenAll(
                prefs.SetProfile(user, other, profile.WithName("Other Spacer").WithBankBalance(1_000)),
                prefs.SetProfile(user, slot, profile));
        });
        await PoolManager.WaitUntil(Server, () => save.IsCompleted, maxTicks: 300);

        EntityUid mine = default;
        EntityUid theirs = default;
        await Server.WaitPost(() =>
        {
            mine = SEntMan.SpawnEntity("PassengerIDCard", SEntMan.GetCoordinates(PlayerCoords));
            theirs = SEntMan.SpawnEntity("PassengerIDCard", SEntMan.GetCoordinates(PlayerCoords));
            banking.Link(mine, user, slot, profile.Name, 3_000_000);
            banking.Link(theirs, user, other, "Other Spacer", 1_000);
            // Without a bank account on the body, the app goes through the profile.
            SEntMan.RemoveComponent<BankAccountComponent>(SPlayer);
        });

        int SlotBalance(int index)
        {
            return ((HumanoidCharacterProfile) prefs.GetPreferences(user).Characters[index]).BankBalance;
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(banking.State(SPlayer, theirs).Status, Is.EqualTo(WGBankStatus.Offline));
            Assert.That(banking.Withdraw(SPlayer, theirs, 100), Is.Not.Null);
            Assert.That(prefs.GetPreferences(user).SelectedCharacterIndex, Is.EqualTo(slot), "Another slot's card doesn't change the played character.");
            Assert.That(SlotBalance(other), Is.EqualTo(1_000));
            Assert.That(banking.State(SPlayer, mine).Status, Is.EqualTo(WGBankStatus.Ready));
        });

        var cfg = Server.CfgMan;
        var threshold = cfg.GetCVar(MonoCVars.DepositThreshold);
        var exponent = cfg.GetCVar(MonoCVars.DepositHighExp);
        try
        {
            await Server.WaitAssertion(() =>
            {
                cfg.SetCVar(MonoCVars.DepositThreshold, 1_000_000f);
                cfg.SetCVar(MonoCVars.DepositHighExp, 1f);
                bank.GetTaxedDepositAmount(50_000, 3_000_000, out var kept, out var taxed);
                Assert.That(taxed, Is.GreaterThan(0));

                var cash = stacks.Spawn(500_000, "Credit", SEntMan.GetCoordinates(PlayerCoords));
                Assert.That(hands.TryPickup(SPlayer, cash), Is.True);
                Assert.That(banking.Deposit(SPlayer, mine, out var deposited), Is.Null);
                Assert.That(deposited, Is.EqualTo(50_000), "Only the round's deposit limit is taken.");
                Assert.That(SlotBalance(slot), Is.EqualTo(3_000_000 + kept), "A deposit through the profile is taxed like one through the body.");
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                cfg.SetCVar(MonoCVars.DepositThreshold, threshold);
                cfg.SetCVar(MonoCVars.DepositHighExp, exponent);
            });
        }

        await Server.WaitPost(() =>
        {
            var current = (HumanoidCharacterProfile) prefs.GetPreferences(user).Characters[slot];
            save = prefs.SetProfile(user, slot, current.WithTraitPreference("Ironman", ProtoMan));
        });
        await PoolManager.WaitUntil(Server, () => save.IsCompleted, maxTicks: 300);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<IronmanComponent>(SPlayer), Is.False);
            Assert.That(banking.State(SPlayer, mine).Status, Is.EqualTo(WGBankStatus.Locked), "An Ironman character is locked without its body.");
            Assert.That(banking.Withdraw(SPlayer, mine, 100), Is.Not.Null);
        });

        await Server.WaitPost(() =>
        {
            var current = (HumanoidCharacterProfile) prefs.GetPreferences(user).Characters[slot];
            save = prefs.SetProfile(user, slot, current.WithoutTraitPreference("Ironman", ProtoMan));
        });
        await PoolManager.WaitUntil(Server, () => save.IsCompleted, maxTicks: 300);
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
                if (!installed.Contains(WGBankSystem.BankCartridge.Id))
                    missing.Add(proto.ID);

                SEntMan.DeleteEntity(pda);
            }

            Assert.That(missing, Is.Empty, "PDAs without the Bank app: " + string.Join(", ", missing));
        });
    }
}
