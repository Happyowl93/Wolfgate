using Content.Client._WF.Administration.RadarTeleport;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.Shuttles.UI;

public sealed partial class RadarConsoleWindow
{
    /// <summary>
    /// Gives an admin ghost a toggle that turns a click on the scanner into a teleport to that spot.
    /// </summary>
    private void InitAdminTeleport()
    {
        var teleport = IoCManager.Resolve<IEntityManager>().System<RadarTeleportSystem>();
        if (!teleport.CanTeleport())
            return;

        var toggle = new Button
        {
            Name = "AdminTeleportToggle",
            Text = Loc.GetString("wf-radar-teleport-toggle"),
            ToolTip = Loc.GetString("wf-radar-teleport-tooltip"),
            TextAlign = Label.AlignMode.Center,
            StyleClasses = { "ButtonSquare" },
            ToggleMode = true,
            HorizontalAlignment = HAlignment.Left,
            Pressed = teleport.Enabled,
        };
        toggle.OnToggled += args => teleport.Enabled = args.Pressed;
        IFFToggle.Parent?.AddChild(toggle);

        RadarScreen.OnKeyBindDown += args =>
        {
            if (args.Function == EngineKeyFunctions.UIClick)
                teleport.RequestTeleport(RadarScreen.GetMouseCoordinatesFromCenter());
        };
    }
}
