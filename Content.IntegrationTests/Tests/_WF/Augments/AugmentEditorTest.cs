using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.Augments.UI;
using Content.Shared._WF.Augments;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>The creator's Augments tab finds an icon for every choice and lays a part's choices out as cards.</summary>
[TestFixture]
[TestOf(typeof(WFAugmentEditor))]
public sealed class AugmentEditorTest
{
    private static IEnumerable<Control> Descendants(Control control)
    {
        return control.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));
    }

    [Test]
    public async Task IconsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var entities = client.ResolveDependency<IEntityManager>();

        await client.WaitAssertion(() =>
        {
            var icons = new WFAugmentIcons();
            var before = entities.EntityCount;
            Assert.Multiple(() =>
            {
                foreach (var augment in proto.EnumeratePrototypes<WFAugmentPrototype>())
                {
                    // Plating and a missing limb install nothing, so there is nothing to draw.
                    if (WFAugmentRules.Installed(augment) != null)
                        Assert.That(icons.Of(augment), Is.Not.Empty, $"{augment.ID} has no icon.");
                }

                foreach (var style in WFAugmentRules.StylesFor(WFAugmentSlot.LeftArm, proto))
                {
                    Assert.That(icons.Of(style, WFAugmentSlot.LeftArm), Has.Count.EqualTo(2), $"{style.ID} draws an arm and a hand.");
                }

                var human = HumanoidCharacterProfile.DefaultWithSpecies("Human");
                Assert.That(icons.Natural(human, WFAugmentSlot.LeftArm), Has.Count.EqualTo(2));
                Assert.That(icons.Natural(human, WFAugmentSlot.Heart), Is.Not.Empty);
            });
            Assert.That(entities.EntityCount, Is.EqualTo(before), "Reading an entity's sprite leaves nothing spawned.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CardsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithAugments(new Dictionary<WFAugmentSlot, WFAugmentChoice>
                {
                    [WFAugmentSlot.LeftArm] = new("WFAugmentCyberLeftArm", "WFAugmentStyleCyborg"),
                })
                .WithAugmentImplants(new HashSet<ProtoId<WFAugmentPrototype>> { "WFAugmentLeftToolArm" });

            var editor = new WFAugmentEditor();
            editor.SetProfile(profile);
            Assert.That(Descendants(editor).OfType<WFAugmentStylePicker>(), Is.Empty, "No part is picked yet.");

            // The loadout lists the arm and its implant; picking the arm lists its choices.
            Assert.That(Descendants(editor).OfType<WFAugmentRowButton>().Count(), Is.EqualTo(2));
            Descendants(editor).OfType<WFAugmentDoll>().Single().Selected = WFAugmentRegion.LeftArm;
            editor.SetProfile(profile);

            var cards = Descendants(editor).OfType<WFAugmentRowButton>().Where(card => card.ToggleMode).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(cards, Has.Count.GreaterThanOrEqualTo(5), "Natural, two arms and the arm's implants.");
                Assert.That(cards.Count(card => card.Pressed), Is.EqualTo(2), "The picked arm and the installed implant are lit.");
                Assert.That(Descendants(editor).OfType<WFAugmentStylePicker>().Count(), Is.EqualTo(1));
                Assert.That(Descendants(editor).OfType<WFAugmentIcon>().Count(), Is.GreaterThanOrEqualTo(cards.Count + 2));
            });
        });

        await pair.CleanReturnAsync();
    }
}
