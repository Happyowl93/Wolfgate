namespace Content.Server._WF.Companies;

/// <summary>An ID card issued to a company member, naming their company and rank on examine.</summary>
[RegisterComponent]
public sealed partial class WFCompanyIdComponent : Component
{
    [DataField]
    public string Company = string.Empty;

    [DataField]
    public string Tag = string.Empty;

    [DataField]
    public string Rank = string.Empty;
}
