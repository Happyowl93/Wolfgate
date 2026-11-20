using Robust.Shared.Input;

namespace Content.Shared._WG.ShipModules;

/// <summary>The module hotkeys, bound in the pilot's shuttle input context.</summary>
[KeyFunctions]
public static class WGShipModuleKeys
{
    public static readonly BoundKeyFunction WGShipModuleFirst = "WGShipModuleFirst";
    public static readonly BoundKeyFunction WGShipModuleSecond = "WGShipModuleSecond";
    public static readonly BoundKeyFunction WGShipModuleThird = "WGShipModuleThird";
    public static readonly BoundKeyFunction WGShipModuleFourth = "WGShipModuleFourth";

    /// <summary>The hotkeys in slot order.</summary>
    public static readonly BoundKeyFunction[] Slots =
    {
        WGShipModuleFirst, WGShipModuleSecond, WGShipModuleThird, WGShipModuleFourth,
    };
}
