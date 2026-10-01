using Content.Shared._WF.ShipShields;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WF.ShipShields;

/// <summary>Uses the shared shield panel at an installed generator.</summary>
[UsedImplicitly]
public sealed class WFShipShieldGeneratorBoundUserInterface : BoundUserInterface
{
    private WFShipShieldGeneratorWindow? _window;

    /// <summary>Binds the panel to its generator.</summary>
    public WFShipShieldGeneratorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFShipShieldGeneratorWindow>();
        _window.ShieldPanel.AllocationRequested += (direction, concentration, arc) =>
            SendMessage(new WFShipShieldSetShuntMessage(direction, concentration, arc));
        _window.ShieldPanel.OnSetEnabled += enabled => SendMessage(new WFShipShieldSetEnabledMessage(enabled));
        if (State is WFShipShieldGeneratorUiState state)
            _window.ShieldPanel.UpdateState(state.ShieldShunt, 0f);
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WFShipShieldGeneratorUiState shield)
            _window?.ShieldPanel.UpdateState(shield.ShieldShunt, 0f);
    }
}
