using Content.Shared._WG.ShipModules;
using Content.Shared.Shuttles.Components;
using Robust.Client.Player;
using Robust.Shared.Input.Binding;

namespace Content.Client._WG.ShipModules;

/// <summary>
/// Sends a pilot's module hotkeys and strip presses to the server, keeps the strips on screen current and plays each
/// firing's jolt.
/// </summary>
public sealed partial class WGShipModuleHelmSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(0.1);

    private readonly HashSet<WGShipModuleBank> _banks = new();
    private readonly List<WGShipModuleBank> _polled = new();
    private TimeSpan _poll;

    public override void Initialize()
    {
        base.Initialize();
        var binds = CommandBinds.Builder;
        for (var i = 0; i < WGShipModuleKeys.Slots.Length; i++)
        {
            var slot = i;
            binds = binds.Bind(WGShipModuleKeys.Slots[i], InputCmdHandler.FromDelegate(_ => FireFlown(slot), handle: true,
                outsidePrediction: true));
        }

        binds.Register<WGShipModuleHelmSystem>();
        InitializeJolt();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGShipModuleHelmSystem>();
    }

    /// <summary>Fires a slot of the ship the local player is piloting.</summary>
    private void FireFlown(int slot)
    {
        if (_player.LocalEntity is { } user
            && TryComp<PilotComponent>(user, out var pilot)
            && pilot.Console is { } console
            && Transform(console).GridUid is { } ship)
            Fire(ship, slot);
    }

    /// <summary>Asks the server to fire one slot of a ship's module strip; it only obeys that ship's pilots.</summary>
    public void Fire(EntityUid ship, int slot)
    {
        if (TryComp<WGShipModulesComponent>(ship, out var modules) && slot >= 0 && slot < modules.Slots.Count)
            RaiseNetworkEvent(new WGShipModuleActivateEvent(modules.Slots[slot].Module));
    }

    public void Track(WGShipModuleBank bank)
    {
        _banks.Add(bank);
    }

    public void Untrack(WGShipModuleBank bank)
    {
        _banks.Remove(bank);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        UpdateShakes(frameTime);
        _poll += TimeSpan.FromSeconds(frameTime);
        if (_poll < PollInterval)
            return;

        _poll = TimeSpan.Zero;
        _polled.Clear();
        _polled.AddRange(_banks);
        foreach (var bank in _polled)
        {
            bank.Refresh();
        }
    }
}
