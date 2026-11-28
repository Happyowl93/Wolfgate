using Robust.Client.GameObjects;

namespace Content.Client.Interaction;

public sealed partial class DragDropSystem
{
    [Dependency] private SpriteSystem _wgSprites = default!;

    /// <summary>
    /// Whether a sprite draws post-shader passes of its own, as a manifested admin does. The legacy PostShader property
    /// reads null under two passes and setting it wipes them, so such a sprite gets no drop highlight.
    /// </summary>
    private bool WgHasOwnPasses(SpriteComponent sprite)
    {
        return _wgSprites.GetPostShaders(sprite).Count > 0
            && sprite.PostShader != _dropTargetInRangeShader
            && sprite.PostShader != _dropTargetOutOfRangeShader;
    }

    /// <summary>Drops the passes a drag shadow copied, whose shaders the dragged sprite disposes and re-adds.</summary>
    private void WgStripPasses(SpriteComponent shadow)
    {
        _wgSprites.ClearPostShaders(shadow);
    }
}
