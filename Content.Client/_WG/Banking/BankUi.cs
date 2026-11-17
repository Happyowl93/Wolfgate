using Content.Client.UserInterface.Fragments;
using Content.Shared._WG.Banking;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Banking;

/// <summary>The Bank PDA app.</summary>
public sealed partial class BankUi : UIFragment
{
    private WGBankPanel? _panel;

    public override Control GetUIFragmentRoot()
    {
        return _panel!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _panel = new WGBankPanel();
        _panel.OnAction += action => userInterface.SendMessage(new CartridgeUiMessage(new WGBankCartridgeMessage(action)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is WGBankUiState cast)
            _panel?.UpdateState(cast);
    }
}
