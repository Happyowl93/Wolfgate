using System.Linq;
using System.Numerics;
using Content.Shared._WF.Drydock;
using Robust.Client.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WF.Drydock;

/// <summary>
/// Builds a crane machine's sprite layers and eases its arm, boom, carriage and tool head toward their targets. The arm
/// extends first; the head unfolds once the arm has settled and folds again before the arm retracts.
/// </summary>
public sealed partial class WFDrydockCraneVisualSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private static readonly ResPath CraneRsi = new("/Textures/_WF/Drydock/crane.rsi");
    private static readonly ResPath MachineRsi = new("/Textures/_WF/Drydock/crane_machine.rsi");

    /// <summary>Arm length below which the arm's end cap and the stowed head are hidden.</summary>
    private const float StowedArm = 0.05f;

    /// <summary>Power ratio below which a crane stalls: it stops moving and its lights go out.</summary>
    private const float StalledRatio = 0.25f;

    private readonly Dictionary<EntityUid, CraneLayers> _cranes = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFDrydockCraneComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFDrydockCraneComponent, ComponentShutdown>(OnShutdown);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<WFDrydockCraneComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var crane, out var sprite))
        {
            if (!_cranes.TryGetValue(uid, out var layers))
                continue;

            var ent = (uid, sprite);
            // Read before easing: the arm may only retract once the boom is folded.
            var folded = crane.VisualAngle == crane.ParkAngle && crane.VisualExtension == crane.SectionLength;

            Ease(crane, frameTime, folded);
            PlaceBoom(ent, crane, layers);
            UpdateArm(ent, crane, layers);
            UpdateHead(ent, crane, layers, folded);
            UpdateBeacons(ent, crane, layers);
        }
    }

    /// <summary>Moves the visual arm, boom, trolley and carriage toward the crane's targets, slowed by its power.</summary>
    private void Ease(WFDrydockCraneComponent crane, float frameTime, bool folded)
    {
        var extension = Math.Clamp(crane.Extension, crane.SectionLength, crane.MaxExtension);

        // Every axis slows with the crane's power, and a stalled crane does not move at all.
        var power = crane.PowerRatio < StalledRatio ? 0f : crane.PowerRatio;
        var step = frameTime * power;

        if (crane.Arm > crane.VisualArm || folded)
            crane.VisualArm = Approach(crane.VisualArm, crane.Arm, crane.ArmSpeed * step);

        // The boom and tool head work only once the arm has settled; until then they fold.
        var settled = crane.VisualArm == crane.Arm;
        crane.VisualAngle = Approach(crane.VisualAngle, settled ? crane.Angle : crane.ParkAngle, crane.SlewSpeed * step);
        crane.VisualExtension = Approach(crane.VisualExtension, settled ? extension : crane.SectionLength, crane.ExtendSpeed * step);
        crane.VisualTrolley = Approach(crane.VisualTrolley, settled ? crane.TrolleyPosition : 0f, crane.TrolleySpeed * step);

        // The carriage rides the arm, so it never passes the arm's end.
        crane.VisualPosition = Math.Clamp(Approach(crane.VisualPosition, crane.Position, crane.Speed * step), 0f, crane.VisualArm);
    }

    /// <summary>Places the boom sections, tip, trolley and carriage along the slewed boom.</summary>
    private void PlaceBoom(Entity<SpriteComponent?> ent, WFDrydockCraneComponent crane, CraneLayers layers)
    {
        var pivot = crane.ArmToLocal(0f, crane.VisualPosition);
        var rad = crane.VisualAngle * (MathF.PI / 180f);
        var dir = crane.ArmToLocal(MathF.Cos(rad), MathF.Sin(rad)) - crane.ArmToLocal(0f, 0f);
        var slide = crane.Sections > 1 ? (crane.VisualExtension - crane.SectionLength) / (crane.Sections - 1) : 0f;

        foreach (var (layer, section, tile) in layers.Boom)
        {
            Place(ent, layer, pivot, dir, section * slide + tile + 1f);
        }

        Place(ent, layers.Tip, pivot, dir, crane.VisualExtension);
        Place(ent, layers.TipBeacon, pivot, dir, crane.VisualExtension);
        Place(ent, layers.Trolley, pivot, dir, Math.Clamp(crane.VisualTrolley, 1f, crane.VisualExtension));

        // The carriage stays square on the arm; the turret on it slews with the boom.
        PlaceAt(ent, layers.Carriage, pivot, Angle.Zero);
        PlaceAt(ent, layers.Turret, pivot, new Angle(dir));
        PlaceAt(ent, layers.CarriageBeacon, pivot, Angle.Zero);
    }

    /// <summary>Shows the arm segments up to its length and moves the end cap and the outermost segment to match.</summary>
    private void UpdateArm(Entity<SpriteComponent?> ent, WFDrydockCraneComponent crane, CraneLayers layers)
    {
        var armShown = Math.Min(layers.Arm.Count, (int) MathF.Ceiling(crane.VisualArm));
        if (armShown != layers.ArmShown)
        {
            layers.ArmShown = armShown;
            for (var i = 0; i < layers.Arm.Count; i++)
            {
                _sprite.LayerSetVisible(ent, layers.Arm[i], i < armShown);
                if (i < armShown)
                {
                    _sprite.LayerSetOffset(ent, layers.Arm[i], crane.ArmToLocal(0f, i + 0.5f));
                }
            }
        }

        // Only the last visible segment can overhang the head; keep it within the arm's end cap.
        if (armShown > 0)
        {
            var last = armShown - 1;
            var along = Math.Max(0.5f, Math.Min(last + 0.5f, crane.VisualArm - 0.5f));
            _sprite.LayerSetOffset(ent, layers.Arm[last], crane.ArmToLocal(0f, along));
        }

        _sprite.LayerSetOffset(ent, layers.ArmEnd, crane.ArmToLocal(0f, crane.VisualArm));
        var endShown = crane.VisualArm >= StowedArm;
        if (layers.EndShown != endShown)
        {
            layers.EndShown = endShown;
            _sprite.LayerSetVisible(ent, layers.ArmEnd, endShown);
        }
    }

    /// <summary>Hides the boom and tool head while they sit stowed inside the machine.</summary>
    private void UpdateHead(Entity<SpriteComponent?> ent, WFDrydockCraneComponent crane, CraneLayers layers, bool folded)
    {
        var stowed = crane.VisualArm < StowedArm && folded;
        var headShown = !stowed;
        if (layers.HeadShown == headShown)
            return;

        layers.HeadShown = headShown;
        SetHeadVisible(ent, layers, headShown);
    }

    /// <summary>Switches the beacons and machine lights on or off as the crane gains or loses power.</summary>
    private void UpdateBeacons(Entity<SpriteComponent?> ent, WFDrydockCraneComponent crane, CraneLayers layers)
    {
        var lit = crane.Active && crane.PowerRatio >= StalledRatio;
        if (layers.BeaconOn == lit)
            return;

        layers.BeaconOn = lit;
        var state = lit ? "beacon_on" : "beacon_off";
        _sprite.LayerSetRsiState(ent, layers.TipBeacon, state);
        _sprite.LayerSetRsiState(ent, layers.CarriageBeacon, state);
        _sprite.LayerSetVisible(ent, layers.MachineLights, lit);
    }

    private void SetHeadVisible(Entity<SpriteComponent?> ent, CraneLayers layers, bool visible)
    {
        foreach (var (layer, _, _) in layers.Boom)
        {
            _sprite.LayerSetVisible(ent, layer, visible);
        }

        _sprite.LayerSetVisible(ent, layers.Tip, visible);
        _sprite.LayerSetVisible(ent, layers.TipBeacon, visible);
        _sprite.LayerSetVisible(ent, layers.Trolley, visible);
        _sprite.LayerSetVisible(ent, layers.Carriage, visible);
        _sprite.LayerSetVisible(ent, layers.Turret, visible);
        _sprite.LayerSetVisible(ent, layers.CarriageBeacon, visible);
    }

    private void OnStartup(Entity<WFDrydockCraneComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp(ent.Owner, out SpriteComponent? sprite))
            return;

        // Drop the prototype's placeholder layers before building the crane.
        var count = sprite.AllLayers.Count();
        for (var i = count - 1; i >= 0; i--)
        {
            _sprite.RemoveLayer((ent.Owner, sprite), i);
        }

        // Start stowed so a crane that arrives mid-job animates out to its targets instead of popping in extended.
        var comp = ent.Comp;
        comp.VisualArm = 0f;
        comp.VisualPosition = 0f;
        comp.VisualAngle = comp.ParkAngle;
        comp.VisualExtension = comp.SectionLength;
        comp.VisualTrolley = 0f;

        var layers = new CraneLayers();
        var owner = (ent.Owner, sprite);

        // The arm is one segment per tile, each centred on its tile; the end cap sits at the arm's head.
        for (var i = 0; i < MathF.Ceiling(comp.MaxArm); i++)
        {
            var segment = AddLayer(owner, CraneRsi, "arm");
            _sprite.LayerSetOffset(owner, segment, comp.ArmToLocal(0f, i + 0.5f));
            layers.Arm.Add(segment);
        }

        layers.ArmEnd = AddLayer(owner, CraneRsi, "arm_end");

        // Innermost section first, so each outer sleeve draws over the one sliding out of it.
        for (var section = comp.Sections - 1; section >= 0; section--)
        {
            var state = section == 0 ? "boom_outer" : section == comp.Sections - 1 ? "boom_inner" : "boom_mid";
            for (var tile = 0; tile < comp.SectionLength; tile++)
            {
                layers.Boom.Add((AddLayer(owner, CraneRsi, state), section, tile));
            }
        }

        layers.Tip = AddLayer(owner, CraneRsi, "boom_tip");
        layers.TipBeacon = AddLayer(owner, CraneRsi, "beacon_off");
        layers.Trolley = AddLayer(owner, CraneRsi, "trolley");
        layers.Carriage = AddLayer(owner, CraneRsi, "carriage");
        layers.Turret = AddLayer(owner, CraneRsi, "turret");
        layers.CarriageBeacon = AddLayer(owner, CraneRsi, "beacon_off");

        // The machine body goes last so it draws over the arm and the stowed head.
        // Mirrored cranes use their own body states, facing -X.
        var body = new Vector2(comp.Mirrored ? -0.5f : 0.5f, 0.5f);
        var machine = AddLayer(owner, MachineRsi, comp.Mirrored ? "machine_mirrored" : "machine");
        layers.MachineLights = AddLayer(owner, MachineRsi, comp.Mirrored ? "machine_lights_mirrored" : "machine_lights");
        _sprite.LayerSetOffset(owner, machine, body);
        _sprite.LayerSetOffset(owner, layers.MachineLights, body);

        // Lights and beacon lenses glow on their own, so they skip scene lighting.
        sprite.LayerSetShader(layers.MachineLights, "unshaded");
        sprite.LayerSetShader(layers.TipBeacon, "unshaded");
        sprite.LayerSetShader(layers.CarriageBeacon, "unshaded");

        _cranes[ent.Owner] = layers;
    }

    private void OnShutdown(Entity<WFDrydockCraneComponent> ent, ref ComponentShutdown args)
    {
        _cranes.Remove(ent.Owner);
    }

    /// <summary>Puts a layer at a distance along a boom direction from the pivot, turned with it.</summary>
    private void Place(Entity<SpriteComponent?> sprite, int layer, Vector2 pivot, Vector2 dir, float distance)
    {
        PlaceAt(sprite, layer, pivot + dir * distance, new Angle(dir));
    }

    private void PlaceAt(Entity<SpriteComponent?> sprite, int layer, Vector2 offset, Angle angle)
    {
        _sprite.LayerSetOffset(sprite, layer, offset);
        _sprite.LayerSetRotation(sprite, layer, angle);
    }

    private int AddLayer(Entity<SpriteComponent?> sprite, ResPath rsi, string state)
    {
        return _sprite.AddLayer(sprite, new SpriteSpecifier.Rsi(rsi, state));
    }

    /// <summary>
    /// Moves <paramref name="current"/> toward <paramref name="target"/> by at most <paramref name="maxDelta"/>.
    /// </summary>
    private static float Approach(float current, float target, float maxDelta)
    {
        if (current < target)
            return MathF.Min(current + maxDelta, target);

        return MathF.Max(current - maxDelta, target);
    }

    /// <summary>
    /// Layer indices for one crane. Boom layers know their section and tile within it. The <c>*Shown</c> and
    /// <c>BeaconOn</c> fields cache the last applied visibility, so a null forces the first update.
    /// </summary>
    private sealed class CraneLayers
    {
        public readonly List<int> Arm = new();
        public readonly List<(int Layer, int Section, int Tile)> Boom = new();
        public int ArmEnd;
        public int Tip;
        public int TipBeacon;
        public int Trolley;
        public int Carriage;
        public int Turret;
        public int CarriageBeacon;
        public int MachineLights;
        public int ArmShown = -1;
        public bool? EndShown;
        public bool? HeadShown;
        public bool? BeaconOn;
    }
}
