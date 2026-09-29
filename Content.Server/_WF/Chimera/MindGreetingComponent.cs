namespace Content.Server._WF.Chimera;

/// <summary>
/// Sends the first player who takes this mob a chat message, such as how to talk as it.
/// </summary>
[RegisterComponent, Access(typeof(MindGreetingSystem))]
public sealed partial class MindGreetingComponent : Component
{
    [DataField(required: true)]
    public LocId Message;

    [ViewVariables]
    public bool Greeted;
}
