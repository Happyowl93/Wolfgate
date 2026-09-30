using Content.Shared.Shuttles.Components;

namespace Content.Server._WF.Shuttles;

/// <summary>
/// IFF flags that docked hosts (Mono's ApplyIFFFlagsToDockedShips, e.g. Helios) added to this grid, so undocking
/// takes back only those.
/// </summary>
[RegisterComponent]
public sealed partial class DockedHostIffFlagsComponent : Component
{
    [ViewVariables]
    public IFFFlags Added;
}
