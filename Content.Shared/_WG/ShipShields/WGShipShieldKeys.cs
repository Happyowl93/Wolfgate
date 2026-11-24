using Robust.Shared.Input;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipShields;

/// <summary>The shield hotkey, bound in the pilot's shuttle input context.</summary>
[KeyFunctions]
public static class WGShipShieldKeys
{
    /// <summary>Raises or lowers the flown ship's shields, as the helm's shield switch does.</summary>
    public static readonly BoundKeyFunction WGToggleShipShields = "WGToggleShipShields";
}

/// <summary>Sent by a pilot pressing the shield hotkey; the server flips the shields of the ship their helm flies.</summary>
[Serializable, NetSerializable]
public sealed class WGShipShieldToggleEvent : EntityEventArgs;
