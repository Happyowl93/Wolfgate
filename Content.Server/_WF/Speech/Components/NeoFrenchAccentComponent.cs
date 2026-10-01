using Content.Server._WF.Speech.EntitySystems;

namespace Content.Server._WF.Speech.Components;

/// <summary>
/// Gives the speaker a thick faux-French accent: French word replacements plus rewritten letters.
/// </summary>
[RegisterComponent]
[Access(typeof(NeoFrenchAccentSystem))]
public sealed partial class NeoFrenchAccentComponent : Component {}
