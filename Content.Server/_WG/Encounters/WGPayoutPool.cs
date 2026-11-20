using Robust.Shared.Configuration;

namespace Content.Server._WG.Encounters;

/// <summary>An hourly payout allowance kept apart from the general encounter one, such as a faction campaign's, and the cvar that caps it.</summary>
public readonly record struct WGPayoutPool(string Id, CVarDef<int> HourlyCap);
