using System.Linq;
using Content.Client.Humanoid;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.Species;

/// <summary>
/// Protogen faces and visors belong to the head and draw at the hand and foot layers they always used, so a
/// replaced hand or foot doesn't take the face with it.
/// </summary>
[TestFixture]
public sealed class ProtogenFaceMarkingsTest
{
    private static readonly string[] Faces =
    {
        "ProtogenLEDFace", "ProtogenLEDFaceNoseless", "ProtogenLEDFaceRound", "ProtoVulpLEDFace", "ProtoVulpLEDFaceLong",
        "ProtoVulpLEDFaceFourEyes", "ProtoVoxLEDFace", "ProtoThavenLEDFace", "ProtoSlimePersonLEDFace", "ProtoKinLEDFace",
        "ProtoResomiLEDFace", "ProtoReptileLEDFaceBoxy", "ProtoMothLEDFace", "ProtoFelineLEDFace", "ProtoDionaeLEDFace",
        "ProtoAvaliLEDFace", "ProtoArachnidLEDFace", "ProtoVulpSnoutLong", "ProtoVoxVisor", "ProtoThavenVisor",
        "ProtoSlimePersonVisor", "ProtoKinVisor", "ProtoResomiVisor", "ProtoMothVisor", "ProtoFelineVisor",
        "ProtoDionaeVisor", "ProtoAvaliVisor", "ProtoArachnidVisor", "ProtogenVisorRound", "ProtoVulpVisor",
        "ProtoVulpVisorFourEyes",
    };

    private static readonly string[] DrawnAt = { "RHand", "RFoot", "LFoot" };

    [Test]
    public async Task FacesBelongToTheHeadTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var markings = pair.Server.ResolveDependency<MarkingManager>();

        await pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var id in Faces)
                {
                    Assert.That(markings.Markings.TryGetValue(id, out var marking), Is.True, $"{id} is gone.");
                    Assert.That(marking!.BodyPart, Is.EqualTo(HumanoidVisualLayers.Head), $"{id} isn't the head's.");
                    foreach (var sprite in marking.Sprites.OfType<SpriteSpecifier.Rsi>())
                    {
                        Assert.That(marking.Layering != null && marking.Layering.TryGetValue(sprite.RsiState, out var layer)
                                    && DrawnAt.Contains(layer), Is.True, $"{id} moved off the layer it draws at.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FaceOutlastsTheHandTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entMan = client.EntMan;
        var markings = client.ResolveDependency<MarkingManager>();

        await client.WaitAssertion(() =>
        {
            var humanoids = entMan.System<HumanoidAppearanceSystem>();
            var sprites = entMan.System<SpriteSystem>();
            var worn = new[] { "ProtogenLEDFace", "ProtogenVisorRound", "ProtogenLights" }
                .Select(id => markings.Markings[id].AsMarking())
                .ToList();
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Protogen");
            profile = profile.WithCharacterAppearance(profile.Appearance.WithMarkings(worn));

            var mob = entMan.SpawnEntity("MobProtogen", MapCoordinates.Nullspace);
            humanoids.LoadProfile(mob, profile);
            var sprite = entMan.GetComponent<SpriteComponent>(mob);
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(mob);

            int Index(string key)
            {
                Assert.That(sprites.LayerMapTryGet((mob, sprite), key, out var i, false), Is.True, $"{key} isn't drawn.");
                return i;
            }

            bool Visible(string key) => sprite[Index(key)].Visible;

            Assert.That(sprites.LayerMapTryGet((mob, sprite), HumanoidVisualLayers.RHand, out var hand, false), Is.True);
            Assert.That(sprites.LayerMapTryGet((mob, sprite), HumanoidVisualLayers.LFoot, out var foot, false), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(Index("ProtogenLEDFace-face"), Is.GreaterThan(hand), "The face moved under the hand.");
                Assert.That(Index("ProtogenVisorRound-rounded"), Is.GreaterThan(foot), "The visor moved under the foot.");
                Assert.That(Index("ProtogenVisorRound-rounded"), Is.LessThan(Index("ProtogenLEDFace-face")), "The visor drew over the face.");
                Assert.That(Visible("ProtogenLEDFace-face"), Is.True);
                Assert.That(Visible("ProtogenVisorRound-rounded"), Is.True);
            });

            // A hand and foot taken off hide their own layers, as a severed or replaced limb does.
            humanoids.SetLayerVisibility((mob, humanoid), HumanoidVisualLayers.RHand, false, null);
            humanoids.SetLayerVisibility((mob, humanoid), HumanoidVisualLayers.LFoot, false, null);
            Assert.Multiple(() =>
            {
                Assert.That(Visible("ProtogenLights-body"), Is.False, "Hiding the hand didn't hide its own markings.");
                Assert.That(Visible("ProtogenLEDFace-face"), Is.True, "The face went with the hand.");
                Assert.That(Visible("ProtogenVisorRound-rounded"), Is.True, "The visor went with the foot.");
            });

            entMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }
}
