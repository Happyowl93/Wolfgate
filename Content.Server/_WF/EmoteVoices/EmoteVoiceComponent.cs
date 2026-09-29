using Content.Shared._WF.EmoteVoices;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.EmoteVoices;

/// <summary>The emote voices a character chose, laid over their species' emote sounds.</summary>
[RegisterComponent]
[Access(typeof(EmoteVoiceSystem))]
public sealed partial class EmoteVoiceComponent : Component
{
    [DataField]
    public List<ProtoId<EmoteVoicePrototype>> Voices = new();
}
