using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Reporting;

/// <summary>s12-04: the records a posture snapshot reads from CISO Assistant besides the audits, as of one read.</summary>
public sealed record PostureRecords(
    IReadOnlyList<ControlItem> Controls,
    IReadOnlyList<AcceptanceItem> Acceptances,
    IReadOnlyList<RemediationItem> Remediation,
    IReadOnlyList<IncidentItem> Incidents,
    IReadOnlyList<RiskItem> Risks,
    IReadOnlyList<DueItem> Due);

/// <summary>Reads the records a posture snapshot needs. Read only: nothing is written.</summary>
public interface IPostureSource
{
    Task<Result<PostureRecords>> ReadAsync(CancellationToken cancellationToken);

    /// <summary>The link to an object in CISO Assistant's web app, such as <c>https://ciso.example.com/applied-controls/&lt;id&gt;</c>.</summary>
    string Link(string resource, string id);
}
