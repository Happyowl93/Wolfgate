using Content.Shared._WF.Narcotics;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Narcotics;

/// <summary>
/// Draws the local player's strongest high and any crash with one shader. Each phase has its own look: light is a
/// steady grade, heavy adds the family's motion, and the overdose pushes it to an extreme; strength builds with the
/// level inside each phase, and stepping up a phase kicks the screen. Static under reduced motion; nothing for a
/// neuroware host, whose state shows on its HUD instead.
/// </summary>
public sealed class WFIntoxicationOverlay : Overlay
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    /// <summary>How fast the drawn intensity follows the target, per second.</summary>
    private const float Follow = 1.5f;

    /// <summary>How fast the heavy and overdose looks come in or go, per second.</summary>
    private const float PhaseFollow = 2.5f;

    /// <summary>How fast a phase kick fades, per second.</summary>
    private const float KickFade = 1.2f;

    /// <summary>How far past its start the overdose tier ramps to full strength, as a share of its start level.</summary>
    private const float OverdoseRamp = 0.5f;

    private readonly ShaderInstance _shader;
    private readonly WFIntoxicationSystem _system;

    private WFDrugFamily _family;
    private WFDrugPhase _phase;
    private float _intensity;
    private float _heavy;
    private float _overdose;
    private float _kick;
    private float _crash;
    private float _pulseRate = 1.2f;

    public WFIntoxicationOverlay(WFIntoxicationSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _shader = _proto.Index<ShaderPrototype>("WFIntoxication").InstanceUnique();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var targetIntensity = 0f;
        var targetCrash = 0f;
        var phase = WFDrugPhase.None;

        if (_player.LocalEntity is { } local
            && _entities.TryGetComponent(local, out WFIntoxicationComponent? comp)
            && !_system.IsNeurowareHost(local))
        {
            foreach (var (profile, level) in comp.Levels)
            {
                if (!_proto.TryIndex(profile, out var proto))
                    continue;

                var tier = proto.TierAt(level);
                var tierPhase = proto.PhaseOf(tier);
                if (tierPhase == WFDrugPhase.None || proto.Tiers[tier].Overlay <= 0f)
                    continue;

                var intensity = Strength(proto, tier, level);
                if (tierPhase < phase || tierPhase == phase && intensity <= targetIntensity)
                    continue;

                phase = tierPhase;
                targetIntensity = intensity;
                _family = proto.Family;
                _pulseRate = 1.2f + 0.6f * tier;
            }

            foreach (var profile in _system.ActiveCrashes(comp))
            {
                if (_proto.TryIndex(profile, out var proto) && proto.Crash != null)
                    targetCrash = Math.Max(targetCrash, proto.Crash.Overlay);
            }
        }

        if (phase > _phase && phase != WFDrugPhase.None)
            _kick = 1f;

        _phase = phase;

        var step = Math.Min(1f, args.DeltaSeconds * Follow);
        _intensity += (targetIntensity - _intensity) * step;
        _crash += (targetCrash - _crash) * step;

        var phaseStep = Math.Min(1f, args.DeltaSeconds * PhaseFollow);
        _heavy += ((phase >= WFDrugPhase.Heavy ? 1f : 0f) - _heavy) * phaseStep;
        _overdose += ((phase == WFDrugPhase.Overdose ? 1f : 0f) - _overdose) * phaseStep;
        _kick = Math.Max(0f, _kick - args.DeltaSeconds * KickFade);
    }

    /// <summary>The look's strength at a level: from this tier's overlay towards the next tier's as the level climbs.</summary>
    private static float Strength(WFDrugProfilePrototype proto, int tier, float level)
    {
        var current = proto.Tiers[tier];
        float next, span;
        if (tier + 1 < proto.Tiers.Count)
        {
            next = proto.Tiers[tier + 1].Overlay;
            span = proto.Tiers[tier + 1].Min - current.Min;
        }
        else
        {
            next = 1f;
            span = current.Min * OverdoseRamp;
        }

        var progress = span > 0f ? Math.Clamp((level - current.Min) / span, 0f, 1f) : 1f;
        return Math.Clamp(current.Overlay + (Math.Max(next, current.Overlay) - current.Overlay) * progress, 0f, 1f);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_intensity < 0.01f && _crash < 0.01f && _kick < 0.01f)
            return false;

        return _entities.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) && args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("family", (float) _family);
        _shader.SetParameter("intensity", _intensity);
        _shader.SetParameter("heavy", _heavy);
        _shader.SetParameter("overdose", _overdose);
        _shader.SetParameter("kick", _kick);
        _shader.SetParameter("crash", _crash);
        _shader.SetParameter("motion", _config.GetCVar(CCVars.ReducedMotion) ? 0f : 1f);
        _shader.SetParameter("pulseRate", _pulseRate);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
