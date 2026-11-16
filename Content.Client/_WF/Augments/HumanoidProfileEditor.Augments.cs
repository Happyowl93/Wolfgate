using System.Linq;
using Content.Shared._WF.Augments;

namespace Content.Client.Lobby.UI;

/// <summary>The Augments tab: cybernetic limbs, organs and implants on a clickable doll.</summary>
public sealed partial class HumanoidProfileEditor
{
    private void InitializeAugments()
    {
        TabContainer.SetTabTitle(AugmentsTab.GetPositionInParent(), Loc.GetString("wf-augments-tab"));
        AugmentEditor.OnAugmentsChanged += (augments, implants) =>
        {
            if (Profile == null)
                return;

            // A styled augment picked without a style gets the first that fits; implants a new limb can't hold drop.
            var valid = WFAugmentRules.Validate(Profile.Species, augments, Profile.TraitPreferences,
                _prototypeManager, _entManager.ComponentFactory);
            Profile = Profile.WithAugments(valid).WithAugmentImplants(WFAugmentRules.ValidateImplants(Profile.Species,
                valid, implants, Profile.TraitPreferences, _prototypeManager, _entManager.ComponentFactory));
            SetDirty();
            AugmentEditor.SetProfile(Profile);
            ReloadProfilePreview();
        };

        // Confirming saves the profile, which buys and sells on the server; same checks as the Save button.
        AugmentEditor.OnConfirm += () =>
        {
            if (Profile == null)
                return;

            if (AnatomyClearedOnSave())
            {
                OpenAnatomySaveConfirm(() => Save?.Invoke());
                return;
            }

            GenitalEditor.ResetPreview();
            Save?.Invoke();
        };
    }

    /// <summary>Drops augments and implants the species or the traits now rule out, and refreshes the tab.</summary>
    private void UpdateAugments()
    {
        if (Profile == null)
            return;

        var valid = WFAugmentRules.Validate(Profile.Species, Profile.Augments, Profile.TraitPreferences,
            _prototypeManager, _entManager.ComponentFactory);
        var implants = WFAugmentRules.ValidateImplants(Profile.Species, valid, Profile.AugmentImplants,
            Profile.TraitPreferences, _prototypeManager, _entManager.ComponentFactory);
        if (valid.Count != Profile.Augments.Count
            || valid.Any(v => !Profile.Augments.TryGetValue(v.Key, out var c) || c != v.Value)
            || !implants.SetEquals(Profile.AugmentImplants))
        {
            Profile = Profile.WithAugments(valid).WithAugmentImplants(implants);
            ReloadProfilePreview();
        }

        AugmentEditor.SetProfile(Profile);
    }
}
