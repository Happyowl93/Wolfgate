using Content.Client._FarHorizons.StarSystem;
using Content.Client.Parallax;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._WF.SectorTravel;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.SectorTravel;

/// <summary>Draws a sector's worlds (<see cref="WFSectorPrototype.Worlds"/>) with the star system planet shader.</summary>
public sealed class WFSectorWorldOverlay : Overlay
{
    private readonly IEntityManager _entMan;
    private readonly IPrototypeManager _proto;
    private readonly PlanetOverlay _planets;
    private readonly List<ShaderInstance> _shaders = new();
    private string? _sector;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public WFSectorWorldOverlay(IEntityManager entMan, IPrototypeManager proto)
    {
        ZIndex = ParallaxSystem.ParallaxZIndex + 2;
        _entMan = entMan;
        _proto = proto;
        _planets = new PlanetOverlay(entMan, proto);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var sector = _entMan.TryGetComponent<WFSectorMapComponent>(args.MapUid, out var stamp) ? stamp.Sector.Id : null;
        if (sector != _sector)
            Build(sector);

        return _shaders.Count > 0;
    }

    private void Build(string? sector)
    {
        _sector = sector;
        _shaders.Clear();
        if (sector == null || !_proto.TryIndex<WFSectorPrototype>(sector, out var proto))
            return;

        foreach (var world in proto.Worlds)
        {
            if (!_proto.TryIndex(world.Planet, out var planetType) || !_proto.TryIndex(world.Light, out var lightType))
                continue;

            var light = new Star(lightType, _proto) { Position = world.LightPosition };
            if (_planets.SetupPlanetShader(new Planet(planetType, _proto, world.Position), light) is { } shader)
                _shaders.Add(shader);
        }
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var bounds = args.WorldAABB;
        var center = args.Viewport.Eye?.Position.Position ?? bounds.Center;
        foreach (var shader in _shaders)
        {
            shader.SetParameter("viewportMin", bounds.BottomLeft);
            shader.SetParameter("viewportSize", bounds.Size);
            shader.SetParameter("parallaxCenter", center);
            handle.UseShader(shader);
            handle.DrawRect(bounds, Color.White);
        }

        handle.UseShader(null);
    }
}
