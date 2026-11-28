namespace Content.Shared.Ame.Components;

public sealed partial class AmeControllerBoundUserInterfaceState
{
    /// <summary>Mean integrity of the engine's cores, in percent; the engine explodes at zero.</summary>
    public int WGStability = 100;

    /// <summary>Highest injection amount that doesn't wear the cores down: two per core.</summary>
    public int WGSafeInjectionLimit;
}
