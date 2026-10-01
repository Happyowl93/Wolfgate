#nullable enable
using System.Numerics;
using Content.Client.Shuttles.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.Administration.Systems;
using Content.Server.Administration.Managers;
using Content.Shared._WF.Administration.RadarTeleport;
using Content.Shared.Administration;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.UserInterface;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Administration;

/// <summary>
/// Mass scanner teleport: an admin ghost with the toggle on is moved to the clicked spot, and nobody else is.
/// </summary>
[TestOf(typeof(RadarTeleportSystem))]
public sealed class RadarTeleportTest : InteractionTest
{
    private const string AdminGhost = "AdminObserver";
    private const string Ghost = "MobObserver";
    private const string Toggle = "AdminTeleportToggle";

    /// <summary>
    /// The toggle is off at first and a click does nothing; with it on, a click moves the ghost to the clicked spot.
    /// </summary>
    [Test]
    public async Task AdminGhostTeleportsToClick()
    {
        // The toggle state outlives the window, and the pooled client outlives the test.
        await Client.WaitPost(() => CEntMan.System<Content.Client._WF.Administration.RadarTeleport.RadarTeleportSystem>().Enabled = false);
        await SetAdmin(true);
        var ghost = await BecomeGhost(AdminGhost);
        var window = await OpenScanner(ghost);
        var radar = GetControlFromField<ShuttleNavControl>("RadarScreen", window);
        var toggle = FindControl(window, Toggle) as Button;
        Assert.That(toggle, Is.Not.Null, "An admin ghost should get the teleport toggle.");
        Assert.That(toggle!.Pressed, Is.False, "The toggle should start off.");

        var start = await Position(ghost);
        await ClickControl(radar);
        await RunTicks(5);
        Assert.That(await Position(ghost), Is.EqualTo(start), "A click with the toggle off shouldn't teleport.");

        await ClickControl(toggle);
        Assert.That(toggle.Pressed, "Clicking the toggle should turn it on.");

        MapCoordinates expected = default;
        await Client.WaitPost(() =>
            expected = CEntMan.System<SharedTransformSystem>().ToMapCoordinates(radar.GetMouseCoordinatesFromCenter()));
        Assert.That((expected.Position - start.Position).Length(), Is.GreaterThan(1f), "The clicked spot should be away from the ghost.");

        await ClickControl(radar);
        await RunTicks(5);
        var end = await Position(ghost);
        Assert.Multiple(() =>
        {
            Assert.That(end.MapId, Is.EqualTo(expected.MapId));
            Assert.That((end.Position - expected.Position).Length(), Is.LessThan(0.01f), "The ghost should be at the clicked spot.");
        });
    }

    /// <summary>
    /// A ghost who isn't an admin gets no toggle, and hand-sent requests from it or from an admin in a body are ignored.
    /// </summary>
    [Test]
    public async Task OnlyAdminGhostsTeleport()
    {
        await SetAdmin(false);
        var body = SPlayer;
        var ghost = await BecomeGhost(Ghost);
        var window = await OpenScanner(ghost);
        Assert.That(FindControl(window, Toggle), Is.Null, "A ghost who isn't an admin shouldn't get the toggle.");

        var start = await Position(ghost);
        await SendRequest(start);
        Assert.That(await Position(ghost), Is.EqualTo(start), "A request from a ghost who isn't an admin should be ignored.");

        await SetAdmin(true);
        await Server.WaitPost(() =>
            SEntMan.System<SharedMindSystem>().TransferTo(ServerSession.ContentData()!.Mind!.Value, body));
        await RunTicks(5);
        Assert.That(ServerSession.AttachedEntity, Is.EqualTo(body), "The player should be back in the body.");

        start = await Position(body);
        await SendRequest(start);
        Assert.That(await Position(body), Is.EqualTo(start), "A request from an admin in a body should be ignored.");
    }

    /// <summary>
    /// Sends a teleport request by hand for a spot 10 m away.
    /// </summary>
    private async Task SendRequest(MapCoordinates from)
    {
        await Client.WaitPost(() =>
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new RadarTeleportRequestEvent(from.Offset(new Vector2(10f, 10f)))));
        await RunTicks(5);
    }

    private async Task<MapCoordinates> Position(EntityUid uid)
    {
        MapCoordinates position = default;
        await Server.WaitPost(() => position = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(uid));
        return position;
    }

    private async Task<RadarConsoleWindow> OpenScanner(EntityUid ghost)
    {
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<IntrinsicUISystem>().InteractUI(ghost, RadarConsoleUiKey.Key), "The ghost's mass scanner should open."));
        await RunTicks(10);
        return GetWindow<RadarConsoleWindow>();
    }

    private static Control? FindControl(Control parent, string name)
    {
        foreach (var child in parent.Children)
        {
            if (child.Name == name)
                return child;

            if (FindControl(child, name) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Makes the player an active admin or not, and waits for the client to hear about it.
    /// </summary>
    private async Task SetAdmin(bool admin)
    {
        var admins = Server.ResolveDependency<IAdminManager>();
        var clientAdmins = Client.ResolveDependency<Content.Client.Administration.Managers.IClientAdminManager>();
        await Server.WaitPost(() =>
        {
            if (!admin)
                admins.DeAdmin(ServerSession);
            else if (admins.IsAdmin(ServerSession, includeDeAdmin: true))
                admins.ReAdmin(ServerSession);
            else
                admins.PromoteHost(ServerSession);
        });

        for (var i = 0; i < 60 && clientAdmins.HasFlag(AdminFlags.Admin) != admin; i++)
        {
            await RunTicks(1);
        }

        Assert.Multiple(() =>
        {
            Assert.That(admins.HasAdminFlag(ServerSession, AdminFlags.Admin), Is.EqualTo(admin));
            Assert.That(clientAdmins.HasFlag(AdminFlags.Admin), Is.EqualTo(admin));
        });
    }

    /// <summary>
    /// Moves the player, with a new mind, into a fresh ghost.
    /// </summary>
    private async Task<EntityUid> BecomeGhost(string prototype)
    {
        EntityUid ghost = default;
        await Server.WaitPost(() =>
        {
            var minds = SEntMan.System<SharedMindSystem>();
            minds.WipeMind(ServerSession.ContentData()?.Mind);
            ghost = SEntMan.SpawnEntity(prototype, SEntMan.GetCoordinates(PlayerCoords));
            minds.TransferTo(minds.CreateMind(ServerSession.UserId).Owner, ghost);
        });

        await RunTicks(5);
        Assert.That(ServerSession.AttachedEntity, Is.EqualTo(ghost), "The player should be the ghost.");
        return ghost;
    }
}
