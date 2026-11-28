using Content.Shared._WG.DockLock;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Shuttles.UI;

public sealed partial class DockingScreen
{
    [Dependency] private IPlayerManager _wgPlayer = default!;

    private static readonly Color WgDockLockedColor = Color.FromHex("#ff6040");

    /// <summary>Adds a port's lock key under its entry in the port list; the server decides and the next state shows it.</summary>
    private void WgAddDockLockToggle(DockingPortState dock)
    {
        var locked = dock.WgDockLocked;
        var toggle = new Button
        {
            Text = Loc.GetString(locked ? "wg-dock-lock-button-on" : "wg-dock-lock-button-off"),
            ToolTip = Loc.GetString("wg-dock-lock-tooltip"),
            HorizontalExpand = true,
            Margin = new Thickness(12f, 0f, 0f, 3f),
        };

        if (locked)
            toggle.ModulateSelfOverride = WgDockLockedColor;

        var entity = dock.Entity;
        toggle.OnPressed += _ => WgSendDockLock(entity, !locked);
        DockPorts.AddChild(toggle);
    }

    /// <summary>Sends the lock request through the helm the local player is piloting.</summary>
    private void WgSendDockLock(NetEntity dock, bool locked)
    {
        if (_wgPlayer.LocalEntity is not { } user
            || !_entManager.TryGetComponent<PilotComponent>(user, out var pilot)
            || pilot.Console is not { } console)
            return;

        _entManager.System<SharedUserInterfaceSystem>()
            .ClientSendUiMessage(console, ShuttleConsoleUiKey.Key, new WGDockLockToggleMessage(dock, locked));
    }
}
