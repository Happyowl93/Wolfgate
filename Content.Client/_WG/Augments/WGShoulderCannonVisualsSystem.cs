using Content.Shared._WG.Augments;
using Robust.Client.GameObjects;

namespace Content.Client._WG.Augments;

/// <summary>Draws a raised shoulder cannon over its body, glowing while it charges.</summary>
public sealed class WGShoulderCannonVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private const string CannonKey = "wg-shoulder-cannon";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShoulderCannonDeployedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WGShoulderCannonDeployedComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WGShoulderCannonDeployedComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<WGShoulderCannonDeployedComponent> ent, ref ComponentStartup args)
    {
        UpdateLayer(ent);
    }

    private void OnState(Entity<WGShoulderCannonDeployedComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateLayer(ent);
    }

    private void UpdateLayer(Entity<WGShoulderCannonDeployedComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var layer = _sprite.LayerMapReserve((ent, sprite), CannonKey);
        _sprite.LayerSetSprite((ent, sprite), layer, ent.Comp.Charging ? ent.Comp.ChargingSprite : ent.Comp.Sprite);
        _sprite.LayerSetVisible((ent, sprite), layer, true);
    }

    private void OnShutdown(Entity<WGShoulderCannonDeployedComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite)
            && _sprite.LayerMapTryGet((ent, sprite), CannonKey, out var layer, false))
            _sprite.LayerSetVisible((ent, sprite), layer, false);
    }
}
