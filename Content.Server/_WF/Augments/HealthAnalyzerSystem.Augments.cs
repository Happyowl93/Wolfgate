using Content.Server._WF.Augments;

namespace Content.Server.Medical;

/// <summary>Gives a scan the list of cyberware in the body.</summary>
public sealed partial class HealthAnalyzerSystem
{
    [Dependency] private WFAugmentSystem _wfAugments = default!;
}
