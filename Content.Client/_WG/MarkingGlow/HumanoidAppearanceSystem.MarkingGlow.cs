using Content.Client._WG.CustomMarkings;
using Content.Shared._WG.MarkingGlow;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client.Humanoid;

public sealed partial class HumanoidAppearanceSystem
{
    /// <summary>Whether a body's marking is drawn unshaded because its player made it glow.</summary>
    private bool MarkingGlows(HumanoidAppearanceComponent humanoid, MarkingPrototype marking)
    {
        return MarkingGlowEnabled && MarkingGlowRules.Glows(humanoid.MarkingGlow, marking);
    }

    /// <summary>
    /// Draws the eyes unshaded while they glow, and lit again once they stop. Erased eyes carry the erase shader's
    /// unshaded twin instead; the custom markings put the matching mask back on once the markings are applied.
    /// </summary>
    private void ApplyEyeGlow(HumanoidAppearanceComponent humanoid, SpriteComponent sprite)
    {
        var index = sprite.LayerMapReserveBlank(HumanoidVisualLayers.Eyes);
        if (MarkingGlowEnabled && humanoid.MarkingGlow.Contains(MarkingGlowRules.Eyes))
        {
            sprite.LayerSetShader(index, SpriteSystem.UnshadedId.Id);
            return;
        }

        if (sprite[index] is SpriteComponent.Layer layer
            && (layer.ShaderPrototype == SpriteSystem.UnshadedId || layer.ShaderPrototype == CustomMarkingSystem.EraseShaderUnshaded))
            sprite.LayerSetShader(index, (ShaderInstance?) null);
    }
}
