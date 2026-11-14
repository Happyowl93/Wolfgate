using Content.Client.UserInterface.Fragments;
using Content.Shared._WF.Banking;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Banking;

/// <summary>The Bank PDA app.</summary>
public sealed partial class BankUi : UIFragment
{
    private WFBankPanel? _panel;

    public override Control GetUIFragmentRoot()
    {
        return _panel!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _panel = new WFBankPanel();
        _panel.OnAction += action => userInterface.SendMessage(new CartridgeUiMessage(new WFBankCartridgeMessage(action)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is WFBankUiState cast)
            _panel?.UpdateState(cast);
    }
}
