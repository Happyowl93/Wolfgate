namespace Content.Server._WF.BoardingAction.Components;

/// <summary>On a ship that can't jump while anything here holds it.</summary>
[RegisterComponent]
public sealed partial class WFFtlDisruptedComponent : Component
{
    /// <summary>What holds the ship (a hacking gun or an NPC ship) and the ship each is on.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, EntityUid> Sources = new();
}
