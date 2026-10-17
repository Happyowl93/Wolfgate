// Mono - file changed
using Content.Shared.Spreader;
using Robust.Shared.Prototypes;

namespace Content.Server.Spreader;

[RegisterComponent]
public sealed partial class SpreaderGridComponent : Component
{
    [DataField]
    public float UpdateAccumulator = 0f;

    [DataField]
    public float UpdateSpacing = 1f;

    // WOLFGATE(Shipyard) START: the queue is rebuilt on grid init and cannot be written, so a grid with a live spreader failed to save
    // [DataField]
    [ViewVariables]
    // WOLFGATE END
    public Dictionary<ProtoId<EdgeSpreaderPrototype>, Queue<Entity<EdgeSpreaderComponent>>> SpreadQueues = new();
}
