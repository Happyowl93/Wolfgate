using Robust.Shared.Configuration;

namespace Content.Shared._WG.CCVar;

/// <summary>Settings for the Bank PDA app's round withdrawal and deposit limits.</summary>
[CVarDefs]
public sealed class BankingCVars
{
    /// <summary>Thalers any account may send out through the app each round, whatever its balance.</summary>
    public static readonly CVarDef<int> WithdrawFloor =
        CVarDef.Create("wg.bank.withdraw_floor", 250000, CVar.SERVERONLY);

    /// <summary>Share of its round-start balance an account may send out each round, where that beats the floor.</summary>
    public static readonly CVarDef<float> WithdrawShare =
        CVarDef.Create("wg.bank.withdraw_share", 0.1f, CVar.SERVERONLY);

    /// <summary>Thalers any account may take in as cash through the app each round.</summary>
    public static readonly CVarDef<int> DepositLimit =
        CVarDef.Create("wg.bank.deposit_limit", 50000, CVar.SERVERONLY);
}
