using System.Numerics;
using Content.Shared._WF.Augments;
using Content.Shared.Body.Components;
using Content.Shared.Body.Prototypes;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

namespace Content.Client._WF.Augments.UI;

/// <summary>One sprite state of an icon and its tint.</summary>
public readonly record struct WFAugmentIconLayer(RSI.State State, Color Color);

/// <summary>
/// A sprite cropped to its opaque pixels and scaled to fill the control, so a limb that takes a corner of its frame
/// reads as large as an item that fills it.
/// </summary>
public sealed class WFAugmentIcon : Control
{
    [Dependency] private IResourceManager _resources = default!;

    /// <summary>The most a sprite pixel is enlarged, so a few pixels of hand don't become a mosaic.</summary>
    private const float MaxScale = 4;

    private IReadOnlyList<WFAugmentIconLayer> _layers = Array.Empty<WFAugmentIconLayer>();
    private UIBox2 _bounds;

    /// <summary>Draws the icon faded, for a choice that can't be taken.</summary>
    public bool Dimmed;

    public WFAugmentIcon(float size, IReadOnlyList<WFAugmentIconLayer>? layers = null)
    {
        IoCManager.InjectDependencies(this);
        SetSize = new Vector2(size);
        MouseFilter = MouseFilterMode.Ignore;
        if (layers != null)
            SetLayers(layers);
    }

    /// <summary>Shows these layers, bottom first.</summary>
    public void SetLayers(IReadOnlyList<WFAugmentIconLayer> layers)
    {
        _layers = layers;
        _bounds = default;
        foreach (var layer in layers)
        {
            var state = layer.State;
            var mask = WFAugmentMask.Of(_resources, state.RSI.Path, state.StateId.Name, state.Size);
            var bounds = mask?.Bounds ?? new UIBox2(0, 0, state.Size.X, state.Size.Y);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;
            _bounds = _bounds.Width <= 0
                ? bounds
                : new UIBox2(Math.Min(_bounds.Left, bounds.Left),
                    Math.Min(_bounds.Top, bounds.Top),
                    Math.Max(_bounds.Right, bounds.Right),
                    Math.Max(_bounds.Bottom, bounds.Bottom));
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_bounds.Width <= 0 || _bounds.Height <= 0)
            return;

        var fit = Math.Min(PixelSize.X / _bounds.Width, PixelSize.Y / _bounds.Height);
        // Whole steps keep pixels even; take one when it costs little of the size.
        var whole = MathF.Floor(fit);
        var scale = Math.Min(whole >= 1 && whole >= fit * 0.8f ? whole : fit, MaxScale * UIScale);
        var origin = ((Vector2) PixelSize - _bounds.Size * scale) / 2;
        origin = new Vector2(MathF.Floor(origin.X), MathF.Floor(origin.Y));

        foreach (var layer in _layers)
        {
            var frame = layer.State.Frame0;
            var region = new UIBox2(_bounds.Left,
                _bounds.Top,
                Math.Min(_bounds.Right, frame.Width),
                Math.Min(_bounds.Bottom, frame.Height));
            if (region.Width <= 0 || region.Height <= 0)
                continue;

            var color = Dimmed ? layer.Color.WithAlpha(layer.Color.A * 0.35f) : layer.Color;
            handle.DrawTextureRectRegion(frame, UIBox2.FromDimensions(origin, region.Size * scale), region, color);
        }
    }
}

/// <summary>Finds the sprite that stands for an augment, a style or a natural part in the creator.</summary>
public sealed class WFAugmentIcons
{
    private static readonly IReadOnlyList<WFAugmentIconLayer> None = Array.Empty<WFAugmentIconLayer>();

    /// <summary>Entity sprites by prototype id; reading one spawns the entity, so each is read once.</summary>
    private static readonly Dictionary<string, List<WFAugmentIconLayer>> Entities = new();

    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IComponentFactory _factory = default!;

    public WFAugmentIcons()
    {
        IoCManager.InjectDependencies(this);
    }

    /// <summary>An augment's icon: its own, else the sprite of the part, organ or autosurgeon it installs.</summary>
    public IReadOnlyList<WFAugmentIconLayer> Of(WFAugmentPrototype augment)
    {
        if (augment.Icon is { } icon && Of(icon) is { Count: > 0 } own)
            return own;
        return WFAugmentRules.Installed(augment) is { } installed ? Of(installed) : None;
    }

    /// <summary>A limb in a style: the style's sprites for the slot's layers.</summary>
    public IReadOnlyList<WFAugmentIconLayer> Of(WFAugmentStylePrototype style, WFAugmentSlot slot)
    {
        var layers = new List<WFAugmentIconLayer>();
        foreach (var layer in WFAugmentRules.Layers(slot))
        {
            if (style.Sprites.TryGetValue(layer, out var sprite))
                AddBaseSprite(layers, sprite, Color.White);
        }

        return layers;
    }

    /// <summary>What a slot holds untouched: the species' limb in the character's skin, or its organ.</summary>
    public IReadOnlyList<WFAugmentIconLayer> Natural(HumanoidCharacterProfile profile, WFAugmentSlot slot)
    {
        if (!_proto.TryIndex<SpeciesPrototype>(profile.Species, out var species))
            return None;

        if (WFAugmentRules.OrganSlot(slot) is { } organ)
        {
            return _proto.TryIndex(species.Prototype, out var mob)
                   && mob.TryComp(out BodyComponent? body, _factory)
                   && body.Prototype is { } bodyId
                   && _proto.TryIndex<BodyPrototype>(bodyId, out var bodyProto)
                   && bodyProto.Slots.TryGetValue(WFAugmentRules.PartSlot(slot), out var part)
                   && part.Organs.TryGetValue(organ, out var organId)
                ? Of(new EntProtoId(organId))
                : None;
        }

        var layers = new List<WFAugmentIconLayer>();
        if (!_proto.TryIndex<HumanoidSpeciesBaseSpritesPrototype>(species.SpriteSet, out var set))
            return layers;

        foreach (var layer in WFAugmentRules.Layers(slot))
        {
            if (set.Sprites.TryGetValue(layer, out var sprite))
                AddBaseSprite(layers, sprite, profile.Appearance.SkinColor);
        }

        return layers;
    }

    private IReadOnlyList<WFAugmentIconLayer> Of(SpriteSpecifier specifier)
    {
        switch (specifier)
        {
            case SpriteSpecifier.Rsi rsi:
                var layers = new List<WFAugmentIconLayer>();
                AddState(layers, rsi, Color.White);
                return layers;
            case SpriteSpecifier.EntityPrototype entity:
                return Of(new EntProtoId(entity.EntityPrototypeId));
            default:
                return None;
        }
    }

    /// <summary>The visible sprite layers of an entity as it spawns.</summary>
    private IReadOnlyList<WFAugmentIconLayer> Of(EntProtoId id)
    {
        if (Entities.TryGetValue(id.Id, out var cached))
            return cached;

        var layers = new List<WFAugmentIconLayer>();
        if (_proto.HasIndex(id))
        {
            var dummy = _entities.SpawnEntity(id, MapCoordinates.Nullspace);
            if (_entities.TryGetComponent(dummy, out SpriteComponent? sprite))
            {
                foreach (var spriteLayer in sprite.AllLayers)
                {
                    if (spriteLayer is SpriteComponent.Layer { Visible: true, ActualRsi: { } rsi } layer
                        && rsi.TryGetState(layer.State, out var state))
                        layers.Add(new WFAugmentIconLayer(state, sprite.Color * layer.Color));
                }
            }

            _entities.DeleteEntity(dummy);
        }

        Entities[id.Id] = layers;
        return layers;
    }

    private void AddBaseSprite(List<WFAugmentIconLayer> layers, string id, Color skin)
    {
        if (_proto.TryIndex<HumanoidSpeciesSpriteLayer>(id, out var sprite) && sprite.BaseSprite is SpriteSpecifier.Rsi rsi)
            AddState(layers, rsi, sprite.MatchSkin ? skin : Color.White);
    }

    private void AddState(List<WFAugmentIconLayer> layers, SpriteSpecifier.Rsi specifier, Color color)
    {
        if (_cache.TryGetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / specifier.RsiPath, out var resource)
            && resource.RSI.TryGetState(specifier.RsiState, out var state))
            layers.Add(new WFAugmentIconLayer(state, color));
    }
}
