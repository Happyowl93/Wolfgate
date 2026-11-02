namespace Content.Client.Players.PlayTimeTracking;

public sealed partial class JobRequirementsManager
{
    /// <summary>Rebuilds open job lists after a faction lock changes.</summary>
    public void FactionLocksChanged() => Updated?.Invoke();
}
