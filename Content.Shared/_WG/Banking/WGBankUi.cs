using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Banking;

/// <summary>Whether the Bank app can open the account on the PDA's ID card, and why not.</summary>
[Serializable, NetSerializable]
public enum WGBankStatus : byte
{
    Ready,

    /// <summary>The PDA holds no ID card.</summary>
    NoId,

    /// <summary>The card was never issued to an account holder, or their character is gone.</summary>
    Unlinked,

    /// <summary>The account holder isn't connected, or is playing another character.</summary>
    Offline,

    /// <summary>An Ironman account, which can't send money out.</summary>
    Locked,
}

[Serializable, NetSerializable]
public enum WGBankEntryKind : byte
{
    Withdrawal,
    Deposit,
    TransferOut,
    TransferIn,
    AtmWithdrawal,
    AtmDeposit,

    /// <summary>Paid from the account at a vendor or console, named in the entry's party.</summary>
    Purchase,

    /// <summary>Any other money taken from the account, such as a fine.</summary>
    Charge,

    /// <summary>Any other money paid into the account, such as wages or a sale.</summary>
    Payment,
}

/// <summary>One line of an account's activity this round.</summary>
[Serializable, NetSerializable]
public sealed class WGBankEntry
{
    public WGBankEntryKind Kind;
    public int Amount;

    /// <summary>The other account of a transfer, or where a purchase was made.</summary>
    public string Party = string.Empty;

    /// <summary>Round time of the transaction.</summary>
    public TimeSpan Time;
}

/// <summary>Everything the Bank app shows for the ID card in its PDA.</summary>
[Serializable, NetSerializable]
public sealed class WGBankUiState : BoundUserInterfaceState
{
    public WGBankStatus Status;
    public string Holder = string.Empty;
    public string Job = string.Empty;

    /// <summary>The account number's last four digits.</summary>
    public string Number = string.Empty;

    public int Balance;

    /// <summary>Most the account may send out this round, by withdrawal or transfer.</summary>
    public int Limit;

    /// <summary>What the account has sent out this round.</summary>
    public int Withdrawn;

    /// <summary>Most cash the account may take in through the app this round.</summary>
    public int DepositLimit;

    /// <summary>Cash the account has taken in through the app this round.</summary>
    public int Deposited;

    /// <summary>Thalers in the viewer's active hand, ready to deposit.</summary>
    public int HandCash;

    public bool DepositEnabled;

    /// <summary>This round's activity, newest first.</summary>
    public List<WGBankEntry> History = new();
}

[Serializable, NetSerializable]
public abstract class WGBankAction;

[Serializable, NetSerializable]
public sealed class WGBankWithdrawAction : WGBankAction
{
    public int Amount;
}

[Serializable, NetSerializable]
public sealed class WGBankTransferAction : WGBankAction
{
    /// <summary>The recipient character's full name, as typed.</summary>
    public string Recipient = string.Empty;
    public int Amount;
}

/// <summary>Deposits the thalers in the viewer's active hand.</summary>
[Serializable, NetSerializable]
public sealed class WGBankDepositAction : WGBankAction;

/// <summary>A Bank app button press.</summary>
[Serializable, NetSerializable]
public sealed class WGBankCartridgeMessage(WGBankAction action) : CartridgeMessageEvent
{
    public WGBankAction Action = action;
}
