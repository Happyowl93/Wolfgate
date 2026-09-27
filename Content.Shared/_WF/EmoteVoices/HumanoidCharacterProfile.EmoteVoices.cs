using Content.Shared._WF.EmoteVoices;
using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences;

/// <summary>The scream and laugh chosen in the creator.</summary>
public sealed partial class HumanoidCharacterProfile
{
    /// <summary>Scream chosen in the creator; null keeps the species' own.</summary>
    [DataField]
    public ProtoId<EmoteVoicePrototype>? ScreamVoice { get; private set; }

    /// <summary>Laugh chosen in the creator; null keeps the species' own.</summary>
    [DataField]
    public ProtoId<EmoteVoicePrototype>? LaughVoice { get; private set; }

    public HumanoidCharacterProfile WithScreamVoice(ProtoId<EmoteVoicePrototype>? voice)
    {
        return new(this) { ScreamVoice = voice };
    }

    public HumanoidCharacterProfile WithLaughVoice(ProtoId<EmoteVoicePrototype>? voice)
    {
        return new(this) { LaughVoice = voice };
    }

    /// <summary>Drops a voice that no longer exists or belongs to another emote.</summary>
    private void EnsureValidEmoteVoices(IPrototypeManager prototypeManager)
    {
        ScreamVoice = EmoteVoiceRules.Sanitize(ScreamVoice, EmoteVoiceRules.Scream, prototypeManager);
        LaughVoice = EmoteVoiceRules.Sanitize(LaughVoice, EmoteVoiceRules.Laugh, prototypeManager);
    }
}
