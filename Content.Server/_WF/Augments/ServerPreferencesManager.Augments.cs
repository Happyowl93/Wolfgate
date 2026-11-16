using System.Linq;
using Content.Server._WF.Augments;
using Content.Shared._WF.Augments;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Preferences.Managers;

public sealed partial class ServerPreferencesManager
{
    [Dependency] private IComponentFactory _wfFactory = default!;

    /// <summary>
    /// Settles the augments of a profile a client saved. Unlocks only come from the stored profile. An unlocked augment
    /// it no longer picks is given up first, sold back for <see cref="WFAugmentRules.RefundPercent"/> of its price if it
    /// was paid for and its hardware hasn't been taken, else for nothing; then a priced augment it hasn't unlocked is
    /// bought from the character's bank if the balance covers it, and dropped otherwise.
    /// </summary>
    /// <returns>Whether anything was bought, sold or dropped, so the client's copy is out of date.</returns>
    private bool SettleAugments(NetUserId userId, int character, ref HumanoidCharacterProfile profile, HumanoidCharacterProfile? stored)
    {
        var hardware = _entityManager.System<WFAugmentSystem>();
        var owned = new HashSet<ProtoId<WFAugmentPrototype>>(stored?.AugmentUnlocks ?? new());
        var found = new HashSet<ProtoId<WFAugmentPrototype>>(stored?.AugmentFound ?? new());
        var unlocks = new HashSet<ProtoId<WFAugmentPrototype>>();
        var balance = profile.BankBalance;
        var name = profile.Name;
        var changed = false;

        // Sells an owned augment back; one never paid for, or whose hardware someone else has, pays nothing.
        void Sell(ProtoId<WFAugmentPrototype> id)
        {
            if (!_protos.TryIndex(id, out var sold))
                return;

            var refund = !found.Contains(id) && hardware.SellPaid(userId, character, id) ? WFAugmentRules.Refund(sold) : 0;
            balance += refund;
            _sawmill.Info($"User {userId} sold augment {id} off {name} for {refund}.");
        }

        var picked = WFAugmentRules.Picked(profile).ToHashSet();
        foreach (var id in owned.Where(o => !picked.Contains(o)).OrderBy(o => o.Id))
        {
            changed = true;
            Sell(id);
        }

        // Buys a priced augment the character doesn't own yet; false if the bank can't cover it.
        bool Buy(WFAugmentPrototype augment)
        {
            if (augment.Price <= 0)
                return true;
            if (owned.Contains(augment.ID))
            {
                unlocks.Add(augment.ID);
                return true;
            }

            changed = true;
            if (balance < augment.Price)
                return false;

            balance -= augment.Price;
            unlocks.Add(augment.ID);
            _sawmill.Info($"User {userId} unlocked augment {augment.ID} on {name} for {augment.Price}.");
            return true;
        }

        var choices = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
        foreach (var (slot, choice) in profile.Augments.OrderBy(c => c.Key))
        {
            if (!_protos.TryIndex(choice.Augment, out var augment))
                changed = true;
            else if (Buy(augment))
                choices[slot] = choice;
        }

        // A limb dropped for want of money takes the implants it had room for.
        var fitting = WFAugmentRules.ValidateImplants(profile.Species, choices, profile.AugmentImplants,
            profile.TraitPreferences, _protos, _wfFactory);
        var implants = new HashSet<ProtoId<WFAugmentPrototype>>();
        foreach (var id in profile.AugmentImplants.OrderBy(i => i.Id))
        {
            if (fitting.Contains(id) && _protos.TryIndex(id, out var implant) && Buy(implant))
                implants.Add(id);
            else
                changed = true;
        }

        // An owned implant that lost its part with that limb is sold too.
        foreach (var id in owned.Where(o => picked.Contains(o) && !unlocks.Contains(o)).OrderBy(o => o.Id))
        {
            Sell(id);
        }

        profile = profile.WithBankBalance(balance)
            .WithAugmentUnlocks(unlocks)
            .WithAugmentFound(found)
            .WithAugments(choices)
            .WithAugmentImplants(implants);
        return changed;
    }

    /// <summary>Sends a player their preferences again, so the creator shows what the server changed.</summary>
    public void ResendPreferences(NetUserId userId)
    {
        if (_cachedPlayerPrefs.TryGetValue(userId, out var data) && data.Prefs is { } prefs)
            ResendPreferences(userId, prefs);
    }

    /// <summary>Sends a player their preferences again, so the creator shows what the server changed.</summary>
    private void ResendPreferences(NetUserId userId, PlayerPreferences prefs)
    {
        if (!_playerManager.TryGetSessionById(userId, out var session))
            return;

        _netManager.ServerSendMessage(new MsgPreferencesAndSettings
        {
            Preferences = prefs,
            Settings = new GameSettings { MaxCharacterSlots = MaxCharacterSlots },
        }, session.Channel);
    }
}
