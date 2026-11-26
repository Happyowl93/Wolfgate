using System.Numerics;
using Content.Client.Shuttles;
using Content.Shared._WG.ShipModules;
using Content.Shared.Shuttles.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WG.ShipModules;

/// <summary>
/// Client effects of ship modules: a skimming hull's ghosts, the rings round guns swung by gyros, the yellow cast on
/// guns an ammo feeder drives, and the tint of thruster plumes burning chemical fuel.
/// </summary>
public sealed partial class WGShipModuleFxSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    /// <summary>Plume colour of thrusters burning frezon.</summary>
    public static readonly Color FueledPlume = Color.FromHex("#8FE3FF");

    /// <summary>Plume colour of converted thrusters with no fuel left.</summary>
    public static readonly Color EmptyPlume = Color.FromHex("#FF7A4D").WithAlpha(0.55f);

    /// <summary>Light yellow cast on guns an ammo feeder drives.</summary>
    public static readonly Color FeederTint = Color.FromHex("#FFF0A8");

    private static readonly TimeSpan PlumeInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>Each tinted gun's own colour, put back when its boost ends.</summary>
    private readonly Dictionary<EntityUid, Color> _feederColors = new();

    /// <summary>Skims still fading, drawn by <see cref="WGShipModuleFxOverlay"/>.</summary>
    public readonly List<Skim> Skims = new();

    /// <summary>Guns spun up by gyros, with when their ring started.</summary>
    public readonly List<(EntityUid Gun, TimeSpan Start)> Swings = new();

    private readonly HashSet<EntityUid> _tinted = new();
    private readonly HashSet<EntityUid> _stillTinted = new();
    private TimeSpan _nextPlumes;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGPhaseSkimEvent>(OnSkim);
        SubscribeNetworkEvent<WGGunGyroSwingEvent>(OnSwing);
        SubscribeLocalEvent<WGAmmoFeederBoostedComponent, ComponentStartup>(OnBoostStart);
        SubscribeLocalEvent<WGAmmoFeederBoostedComponent, ComponentShutdown>(OnBoostEnd);
        _overlays.AddOverlay(new WGShipModuleFxOverlay(this));
    }

    /// <summary>Tints a gun an ammo feeder drives, keeping its own colour to put back.</summary>
    private void OnBoostStart(Entity<WGAmmoFeederBoostedComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        _feederColors[ent] = sprite.Color;
        _sprite.SetColor((ent, sprite), sprite.Color * FeederTint);
    }

    private void OnBoostEnd(Entity<WGAmmoFeederBoostedComponent> ent, ref ComponentShutdown args)
    {
        if (_feederColors.Remove(ent, out var color) && TryComp<SpriteComponent>(ent, out var sprite))
            _sprite.SetColor((ent, sprite), color);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WGShipModuleFxOverlay>();
    }

    private void OnSkim(WGPhaseSkimEvent ev)
    {
        if (TryGetEntity(ev.Ship, out var ship))
            Skims.Add(new Skim(ship.Value, ev.From, ev.FromRotation, ev.To, Transform(ship.Value).MapID, _timing.RealTime));
    }

    private void OnSwing(WGGunGyroSwingEvent ev)
    {
        if (TryGetEntity(ev.Gun, out var gun))
            Swings.Add((gun.Value, _timing.RealTime));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var now = _timing.RealTime;
        Skims.RemoveAll(s => now - s.Start > Skim.Life || Deleted(s.Ship));
        Swings.RemoveAll(s => now - s.Start > SwingLife || Deleted(s.Gun));

        if (now < _nextPlumes)
            return;

        _nextPlumes = now + PlumeInterval;
        TintPlumes();
    }

    /// <summary>Seconds a gyro ring lasts.</summary>
    public static readonly TimeSpan SwingLife = TimeSpan.FromSeconds(0.7);

    /// <summary>Colours the plumes of every thruster on a hull burning chemical fuel, and restores those no longer on one.</summary>
    private void TintPlumes()
    {
        _stillTinted.Clear();
        var grids = EntityQueryEnumerator<WGChemicalThrustComponent, TransformComponent>();
        while (grids.MoveNext(out _, out var chemical, out var xform))
        {
            var color = chemical.Fueled ? FueledPlume : EmptyPlume;
            var children = xform.ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                // Most of a hull has no appearance; only thrusters carry this key.
                if (!TryComp<AppearanceComponent>(child, out var appearance)
                    || !_appearance.TryGetData<bool>(child, ThrusterVisualState.State, out _, appearance))
                    continue;

                SetPlumeColor(child, color);
                _stillTinted.Add(child);
            }
        }

        foreach (var thruster in _tinted)
        {
            if (!_stillTinted.Contains(thruster) && !Deleted(thruster))
                SetPlumeColor(thruster, Color.White);
        }

        _tinted.Clear();
        _tinted.UnionWith(_stillTinted);
    }

    private void SetPlumeColor(EntityUid thruster, Color color)
    {
        if (!TryComp<SpriteComponent>(thruster, out var sprite))
            return;

        Entity<SpriteComponent?> ent = (thruster, sprite);
        if (_sprite.LayerMapTryGet(ent, ThrusterVisualLayers.Thrusting, out var layer, false))
            _sprite.LayerSetColor(ent, layer, color);
        if (_sprite.LayerMapTryGet(ent, ThrusterVisualLayers.ThrustingUnshaded, out layer, false))
            _sprite.LayerSetColor(ent, layer, color);
    }

    /// <summary>One skim: where the hull left, where it came back and when.</summary>
    public readonly record struct Skim(EntityUid Ship, Vector2 From, Angle FromRotation, Vector2 To, MapId Map, TimeSpan Start)
    {
        /// <summary>How long the ghosts and the shimmer last.</summary>
        public static readonly TimeSpan Life = TimeSpan.FromSeconds(0.9);
    }
}
