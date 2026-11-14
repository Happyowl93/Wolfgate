using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>Settings for the Bank PDA app's round withdrawal limit.</summary>
[CVarDefs]
public sealed class BankingCVars
{
    /// <summary>Thalers any account may send out through the app each round, whatever its balance.</summary>
    public static readonly CVarDef<int> WithdrawFloor =
        CVarDef.Create("wf.bank.withdraw_floor", 250000, CVar.SERVERONLY);

    /// <summary>Share of its round-start balance an account may send out each round, where that beats the floor.</summary>
    public static readonly CVarDef<float> WithdrawShare =
        CVarDef.Create("wf.bank.withdraw_share", 0.1f, CVar.SERVERONLY);
}
