using Content.Client._WF.CargoLoad;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    /// <summary>The cargo strip for the ship this screen shows; the prefix names it, as the fuel bank's does.</summary>
    public Control WfCargoGauge(string prefix) => new WFCargoGauge(() => _shuttleEntity, prefix);
}
