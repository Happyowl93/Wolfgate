#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>
    /// A crew walled off from a hostile it has been turned on, two of its five deckhands then downed. Returns those
    /// left standing, the officer a tier above them first if there is one.
    /// </summary>
    private async Task<(EntityUid Deck, EntityUid Hostile, List<EntityUid> Standing)> LosingCrew(Vector2 origin, string group,
        WGCrewSkill skill, bool officer)
    {
        var deck = await CreateDeck(origin, 13, gravity: true);
        EntityUid hostile = default;
        var standing = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 13; x++)
                SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(x + 0.5f, 6.5f)));
            hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(6.5f, 10.5f)));

            if (officer)
            {
                var lead = ConvoyCrew(deck, "WGCrewDeckhand", group, new Vector2(7.5f, 4.5f));
                SEntMan.GetComponent<WGCrewComponent>(lead).SkillOffset = 1;
                standing.Add(lead);
            }

            foreach (var x in new[] { 1.5f, 4.5f, 7.5f })
                standing.Add(ConvoyCrew(deck, "WGCrewDeckhand", group, new Vector2(x, 1.5f)));
            var lost = new[]
            {
                ConvoyCrew(deck, "WGCrewDeckhand", group, new Vector2(10.5f, 1.5f)),
                ConvoyCrew(deck, "WGCrewDeckhand", group, new Vector2(10.5f, 4.5f)),
            };
            foreach (var member in standing.Concat(lost))
                SEntMan.GetComponent<WGCrewComponent>(member).Skill = skill;

            Server.System<WGCrewRallySystem>().Rally(deck, group, hostile);
            var weapons = Server.System<WGCrewWeaponSystem>();
            foreach (var member in standing)
            {
                Assert.That(weapons.CanSee(member, hostile), Is.False, "Precondition: the wall hides the hostile.");
                Assert.That(weapons.Assess(member, hostile), Is.EqualTo(WGCrewEngage.Engage),
                    "Precondition: before any loss the crew goes looking for him.");
            }

            var mobs = Server.System<MobStateSystem>();
            foreach (var member in lost)
                mobs.ChangeMobState(member, MobState.Critical);
            foreach (var member in standing)
                SEntMan.GetComponent<HTNComponent>(member).Enabled = true;
        });

        return (deck, hostile, standing);
    }

    /// <summary>
    /// A veteran crew that has lost two of its own stops hunting: its fighters gather at a rally point with weapons
    /// drawn and cover the way in, then go after the hostile together once he has left them alone.
    /// </summary>
    [Test]
    public async Task VeteranCrewMusterThenPushTogether()
    {
        var muster = Server.System<WGCrewMusterSystem>();
        var weapons = Server.System<WGCrewWeaponSystem>();
        var hands = Server.System<SharedHandsSystem>();
        var (_, hostile, crew) = await LosingCrew(new Vector2(2000, 500), "muster", WGCrewSkill.Veteran, officer: false);

        await WaitUntil(() => crew.All(member => muster.Posture(member) == WGCrewMusterPosture.Hold), 120,
            () => $"Two down with veterans standing: the crew should muster. {Describe(crew[0])}");
        await Server.WaitAssertion(() =>
        {
            foreach (var member in crew)
            {
                Assert.That(weapons.Assess(member, hostile), Is.EqualTo(WGCrewEngage.Brace), "Nobody hunts from a hold.");
                Assert.That(muster.TryGetPlace(member, out _), Is.True, "Each fighter has a place in the hold.");
            }
        });

        bool InPlace(EntityUid member)
        {
            return muster.TryGetPlace(member, out var place)
                   && SEntMan.GetComponent<TransformComponent>(member).Coordinates.TryDistance(SEntMan, place, out var distance)
                   && distance <= 1f
                   && hands.TryGetActiveItem(member, out var held) && SEntMan.HasComponent<GunComponent>(held.Value);
        }

        await WaitUntil(() => crew.All(InPlace), 600,
            () => $"The crew should gather at the rally point with guns drawn. {string.Join(" | ", crew.Select(Describe))}");

        await WaitUntil(() => crew.All(member => muster.Posture(member) == WGCrewMusterPosture.Push), 1500,
            () => $"Left alone, the gathered crew should push. {Describe(crew[0])}");
        await Server.WaitAssertion(() =>
        {
            foreach (var member in crew)
                Assert.That(weapons.Assess(member, hostile), Is.EqualTo(WGCrewEngage.Engage), "The push takes everyone.");
        });
    }

    /// <summary>A crew with nobody of a skill to call it together fights on as before.</summary>
    [Test]
    public async Task RegularCrewDoNotMuster()
    {
        var muster = Server.System<WGCrewMusterSystem>();
        var (_, _, crew) = await LosingCrew(new Vector2(2000, 600), "nomuster", WGCrewSkill.Regular, officer: false);
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            foreach (var member in crew)
                Assert.That(muster.Posture(member), Is.EqualTo(WGCrewMusterPosture.None), Describe(member));
        });
    }

    /// <summary>An officer a tier above a regular crew musters it, and the muster ends when he goes down.</summary>
    [Test]
    public async Task MusterEndsWithItsLeader()
    {
        var muster = Server.System<WGCrewMusterSystem>();
        var (_, _, crew) = await LosingCrew(new Vector2(2000, 700), "leader", WGCrewSkill.Regular, officer: true);
        var hand = crew[1];
        await WaitUntil(() => muster.Posture(hand) == WGCrewMusterPosture.Hold, 120,
            () => $"The officer should muster his crew. {Describe(hand)}");

        await Server.WaitPost(() => Server.System<MobStateSystem>().ChangeMobState(crew[0], MobState.Critical));
        await WaitUntil(() => crew.All(member => muster.Posture(member) == WGCrewMusterPosture.None), 120,
            () => $"With the officer down nobody holds the crew together. {Describe(hand)}");
    }
}
