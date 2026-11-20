using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WG.AdsIncursion.Systems;
using Content.Server._WG.Encounters;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Server._WG.SectorControl;
using Content.Shared._WG.AdsIncursion;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.ReaverCampaign;
using Content.Shared._WG.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.AdsIncursion;

/// <summary>
/// Reaver and ADS bounties each draw on an hourly payout allowance of their own, apart from each other and from the
/// general one other encounter rewards share.
/// </summary>
public sealed class WGCampaignPayoutCapTest : WGAdsTestBase
{
    [TearDown]
    public async Task RestoreGeneralCap()
    {
        await Server.WaitPost(() => Server.ResolveDependency<IConfigurationManager>()
            .SetCVar(EncountersCVars.PayoutHourlyCap, EncountersCVars.PayoutHourlyCap.DefaultValue));
    }

    /// <summary>Both campaigns cap at 450k each by default, with pools of their own.</summary>
    [Test]
    public void CampaignPoolsAreSeparate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ReaverCampaignCVars.PayoutHourlyCap.DefaultValue, Is.EqualTo(450000));
            Assert.That(AdsIncursionCVars.PayoutHourlyCap.DefaultValue, Is.EqualTo(450000));
            Assert.That(WGReaverCampaignSystem.PayoutPool.HourlyCap, Is.SameAs(ReaverCampaignCVars.PayoutHourlyCap));
            Assert.That(WGAdsCampaignSystem.PayoutPool.HourlyCap, Is.SameAs(AdsIncursionCVars.PayoutHourlyCap));
            Assert.That(WGReaverCampaignSystem.PayoutPool.Id, Is.Not.EqualTo(WGAdsCampaignSystem.PayoutPool.Id));
            Assert.That(WGReaverCampaignSystem.PayoutPool.Id, Is.Not.Empty, "Not the general pool.");
            Assert.That(WGAdsCampaignSystem.PayoutPool.Id, Is.Not.Empty, "Not the general pool.");
        });
    }

    /// <summary>A pool's allowance runs out at its own cap while other pools and the general allowance still pay.</summary>
    [Test]
    public async Task EachPoolRunsOutOnItsOwn()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            var rewards = Server.System<WGEncounterRewardSystem>();
            var ship = SpawnGrid(CentreOf(AdsHome) + new Vector2(500f, 0f), "Test Hauler");
            Board(ship);

            // Fresh pools, so allowance a pooled pair counted in earlier tests can't interfere.
            var first = new WGPayoutPool($"test-{Guid.NewGuid()}", ReaverCampaignCVars.PayoutHourlyCap);
            var second = new WGPayoutPool($"test-{Guid.NewGuid()}", AdsIncursionCVars.PayoutHourlyCap);
            config.SetCVar(ReaverCampaignCVars.PayoutHourlyCap, 1000);
            config.SetCVar(AdsIncursionCVars.PayoutHourlyCap, 1000);
            config.SetCVar(EncountersCVars.PayoutHourlyCap, int.MaxValue);

            int Pay(int spesos, WGPayoutPool? pool)
            {
                var before = Balance();
                rewards.PayCrews("test", "test", spesos, new[] { ship }, "testing", pool);
                return Balance() - before;
            }

            Assert.That(Pay(800, first), Is.EqualTo(800));
            Assert.That(Pay(800, first), Is.EqualTo(200), "The allowance runs out at its cap.");
            Assert.That(Pay(800, first), Is.Zero);
            Assert.That(Pay(800, second), Is.EqualTo(800), "Another pool has an allowance of its own.");
            Assert.That(Pay(5000, null), Is.EqualTo(5000), "Rewards without a pool draw on the general allowance.");
        });
    }

    /// <summary>A Reaver payout follows the Reaver cap, not the ADS or general one.</summary>
    [Test]
    public async Task ReaverPayoutsUseTheReaverCap()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            var ship = SpawnGrid(CentreOf(Home.Neighbour(1)) + new Vector2(500f, 0f), "Test Hauler");
            Board(ship);
            config.SetCVar(EncountersCVars.PayoutHourlyCap, int.MaxValue);

            config.SetCVar(ReaverCampaignCVars.PayoutHourlyCap, 0);
            Assert.That(Retake(Reavers, Home.Neighbour(1), ship), Is.Zero, "The Reaver cap holds the payout back.");

            config.SetCVar(ReaverCampaignCVars.PayoutHourlyCap, int.MaxValue);
            config.SetCVar(AdsIncursionCVars.PayoutHourlyCap, 0);
            Assert.That(Retake(Reavers, Home.Neighbour(2), ship), Is.EqualTo(RetakeBounties[0]), "A spent ADS allowance doesn't hold it back.");
        });
    }

    /// <summary>An ADS payout follows the ADS cap, not the Reaver or general one.</summary>
    [Test]
    public async Task AdsPayoutsUseTheAdsCap()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            var ship = SpawnGrid(CentreOf(AdsHome) + new Vector2(500f, 0f), "Test Hauler");
            Board(ship);
            config.SetCVar(EncountersCVars.PayoutHourlyCap, int.MaxValue);

            config.SetCVar(AdsIncursionCVars.PayoutHourlyCap, 0);
            Assert.That(Retake(Ads, AdsHome.Neighbour(1), ship), Is.Zero, "The ADS cap holds the payout back.");

            config.SetCVar(AdsIncursionCVars.PayoutHourlyCap, int.MaxValue);
            config.SetCVar(ReaverCampaignCVars.PayoutHourlyCap, 0);
            Assert.That(Retake(Ads, AdsHome.Neighbour(2), ship), Is.EqualTo(RetakeBounties[0]), "A spent Reaver allowance doesn't hold it back.");
        });
    }

    /// <summary>Raises a faction's cell as retaken by a ship and returns what the player aboard was paid.</summary>
    private int Retake(string faction, WGSectorCell cell, EntityUid ship)
    {
        var before = Balance();
        var retaken = new WGSectorCellRetakenEvent(MapData.MapId, cell, faction, new[] { ship });
        SEntMan.EventBus.RaiseEvent(EventSource.Local, ref retaken);
        return Balance() - before;
    }
}
