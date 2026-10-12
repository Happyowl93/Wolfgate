using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// Drydock repair and refit console. Its berth is the set of cranes linked to its source port with a multitool.
/// </summary>
[RegisterComponent]
public sealed partial class WFDrydockConsoleComponent : Component
{
    /// <summary>Source port the console links its cranes on.</summary>
    public const string CranePort = "WFDrydockCraneSender";

    /// <summary>Sink port a crane takes its console's link on.</summary>
    public const string CraneReceiverPort = "WFDrydockCraneReceiver";

    /// <summary>Name the berth goes by in messages. Empty: the console's entity name.</summary>
    [DataField]
    public string Berth = string.Empty;

    /// <summary>Tiles of guide lights outside the bay.</summary>
    [DataField]
    public float ApproachLength = 16f;

    /// <summary>Seconds the berth lights stay on after they are switched on.</summary>
    [DataField]
    public float BerthLightsTime = 120f;

    /// <summary>When the berth lights go out, or null while they are off. Runtime only.</summary>
    public TimeSpan? BerthLightsUntil;

    /// <summary>Last console state pushed to viewers, as a key. Runtime only.</summary>
    public string? LastPushed;

    /// <summary>Flat fee per repair job, never refunded.</summary>
    [DataField]
    public int BaseFee = 2500;

    /// <summary>Price per missing hull tile.</summary>
    [DataField]
    public int TilePrice = 150;

    /// <summary>Share of an entity's estimated prototype price charged to restore it.</summary>
    [DataField]
    public float EntityPriceFactor = 1.0f;

    /// <summary>Minimum price for restoring one entity.</summary>
    [DataField]
    public int MinEntityPrice = 600;

    /// <summary>Price per point of damage healed.</summary>
    [DataField]
    public float DamagePrice = 5f;

    /// <summary>Seconds one crane needs to restore a hull tile.</summary>
    [DataField]
    public float TileWorkTime = 4f;

    /// <summary>Seconds one crane needs to restore a missing entity.</summary>
    [DataField]
    public float EntityWorkTime = 15f;

    /// <summary>Seconds one crane needs to heal a damaged part.</summary>
    [DataField]
    public float HealWorkTime = 6f;

    /// <summary>Seconds one crane needs to refit a part by one tier.</summary>
    [DataField]
    public float UpgradeWorkTime = 60f;

    /// <summary>Longest job duration in seconds; longer jobs scale every item's work time down.</summary>
    [DataField]
    public float MaxDuration = 1800f;

    /// <summary>Seconds every berth crane may stay unpowered during a job before it is aborted and refunded.</summary>
    [DataField]
    public float PowerAbortTime = 60f;

    /// <summary>Seconds a crane keeps trying for an out-of-reach item before it is skipped and refunded.</summary>
    [DataField]
    public float ReachTimeout = 10f;

    /// <summary>Tiles of slack when deciding whether a crane can reach a point.</summary>
    [DataField]
    public float ReachTolerance = 1f;

    /// <summary>Seconds between sparks at the item being worked on.</summary>
    [DataField]
    public float WorkSparkInterval = 0.35f;

    /// <summary>Seconds between tool sounds at the item being worked on.</summary>
    [DataField]
    public float WorkSoundInterval = 1.3f;

    /// <summary>Seconds between machinery sounds elsewhere on the ship during a job.</summary>
    [DataField]
    public float IndustrySoundInterval = 2.2f;

    /// <summary>Fastest a ship may move relative to the drydock, in m/s, for work to start.</summary>
    [DataField]
    public float MaxStartSpeed = 1f;

    /// <summary>Seconds between random sparks elsewhere on the ship during a job.</summary>
    [DataField]
    public float AmbientSparkInterval = 1.5f;

    /// <summary>Spark effect played where work is applied.</summary>
    [DataField]
    public EntProtoId SparkEffect = "EffectSparks";

    /// <summary>Tool sounds while a crane restores a hull tile: welding and grinding.</summary>
    [DataField]
    public SoundSpecifier HullWorkSound = new SoundCollectionSpecifier("WFDrydockHullWork");

    /// <summary>Tool sounds while a crane rebuilds a missing part: bolt drivers, ratchets and welding.</summary>
    [DataField]
    public SoundSpecifier PartWorkSound = new SoundCollectionSpecifier("WFDrydockPartWork");

    /// <summary>Tool sounds while a crane patches a damaged part: welding, grinding and hammering.</summary>
    [DataField]
    public SoundSpecifier DamageWorkSound = new SoundCollectionSpecifier("WFDrydockDamageWork");

    /// <summary>Tool sounds while a crane refits a machine: drills, ratchets and screwdrivers.</summary>
    [DataField]
    public SoundSpecifier RefitWorkSound = new SoundCollectionSpecifier("WFDrydockRefitWork");

    /// <summary>Machinery clanks, presses and grinders at random spots on the ship during a job.</summary>
    [DataField]
    public SoundSpecifier IndustrySound = new SoundCollectionSpecifier("WFDrydockIndustry", AudioParams.Default.WithVolume(-4f));

    /// <summary>Hydraulics played when a crane starts moving.</summary>
    [DataField]
    public SoundSpecifier CraneMoveSound = new SoundCollectionSpecifier("WFDrydockCraneMove", AudioParams.Default.WithVolume(-3f));

    /// <summary>Played when a crane's tool head arrives at its item.</summary>
    [DataField]
    public SoundSpecifier CraneArriveSound = new SoundCollectionSpecifier("MetalThud", AudioParams.Default.WithVolume(-4f));

    /// <summary>Sound played when a request is refused.</summary>
    [DataField]
    public SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    /// <summary>Sound played when a job finishes.</summary>
    [DataField]
    public SoundSpecifier ConfirmSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    /// <summary>Refit upgrades this console offers.</summary>
    [DataField]
    public List<ProtoId<WFDrydockUpgradePrototype>> Upgrades = new();
}
