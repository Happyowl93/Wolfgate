using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// A ship's fitted modules as its helm shows them, kept on the grid by the server. Absent on a ship with none.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WGShipModulesComponent : Component
{
    /// <summary>Modules a helm can show and fire, one per hotkey.</summary>
    public const int MaxSlots = 4;

    /// <summary>The ship's modules in hotkey order: one per kind, oldest fitting first.</summary>
    [ViewVariables, AutoNetworkedField]
    public List<WGShipModuleSlot> Slots = new();

    /// <summary>Server only: when a shield field was last seen raised on this ship.</summary>
    [ViewVariables]
    public TimeSpan? LastShielded;
}

/// <summary>One module on the helm's strip.</summary>
[Serializable, NetSerializable]
public record struct WGShipModuleSlot(
    NetEntity Module,
    string Label,
    int Charges,
    int MaxCharges,
    TimeSpan NextCharge,
    TimeSpan RechargeTime,
    TimeSpan ReadyAt,
    WGShipModuleLock Lock);

/// <summary>Why a module can't be fired right now, whatever its charges.</summary>
[Serializable, NetSerializable]
public enum WGShipModuleLock : byte
{
    None,
    Unpowered,
    Shields,
    Ftl,
    Docked,
    Anchored,
}

/// <summary>A pilot asks to fire one of the modules of the ship they are flying.</summary>
[Serializable, NetSerializable]
public sealed class WGShipModuleActivateEvent : EntityEventArgs
{
    public NetEntity Module;

    public WGShipModuleActivateEvent(NetEntity module)
    {
        Module = module;
    }
}

/// <summary>A module fired: helms showing its ship light its key and shake, and the crew's view jolts.</summary>
[Serializable, NetSerializable]
public sealed class WGShipModuleFiredEvent : EntityEventArgs
{
    public NetEntity Ship;
    public NetEntity Module;
    public float Jolt;

    public WGShipModuleFiredEvent(NetEntity ship, NetEntity module, float jolt)
    {
        Ship = ship;
        Module = module;
        Jolt = jolt;
    }
}
