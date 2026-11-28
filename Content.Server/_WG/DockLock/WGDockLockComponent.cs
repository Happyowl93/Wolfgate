namespace Content.Server._WG.DockLock;

/// <summary>On a docking port its crew locked: it takes no new docking and its own helm won't undock it.</summary>
[RegisterComponent]
public sealed partial class WGDockLockComponent : Component;
