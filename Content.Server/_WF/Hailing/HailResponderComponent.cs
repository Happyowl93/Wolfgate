namespace Content.Server._WF.Hailing;

/// <summary>
/// An NPC that mans a shuttle console on its grid and answers hails there, greeting the caller and opening the
/// hail services on its grid.
/// </summary>
[RegisterComponent]
public sealed partial class HailResponderComponent : Component
{
    /// <summary>Greetings spoken on answering, picked at random.</summary>
    [DataField]
    public List<LocId> Greetings = new();

    /// <summary>Time a ringing hail is answered after it starts.</summary>
    [DataField]
    public float AnswerDelay = 2f;

    /// <summary>Metres to the console it mans.</summary>
    [DataField]
    public float ConsoleRange = 1.5f;

    /// <summary>The console it mans, once found.</summary>
    [ViewVariables]
    public EntityUid? Console;

    /// <summary>Whether it has greeted the current caller.</summary>
    [ViewVariables]
    public bool Greeted;
}
