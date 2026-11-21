using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Collecting;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Collectors;

/// <summary>
/// The built-in <c>fake</c> collector: fixed, fictional results for tests, demos and dry runs. It reports MFA and backup checks
/// that match <c>mappings/example.yaml</c>, one JSON evidence file per check, and two control statuses. Its files do not change
/// between runs, so publishing twice uploads nothing new.
/// </summary>
public sealed class FakeCollector : ICollector
{
    private static readonly (string CheckId, CheckOutcome Outcome, string Observation, string File, JsonObject Detail)[] Findings =
    [
        ("identity.mfa.admins", CheckOutcome.Pass, "All 4 admin accounts use MFA.", "mfa-admins.json", new JsonObject { ["admins"] = 4, ["with_mfa"] = 4 }),
        ("identity.mfa.users", CheckOutcome.Fail, "3 of 40 user accounts have no MFA method.", "mfa-users.json", new JsonObject { ["users"] = 40, ["with_mfa"] = 37 }),
        ("backup.daily", CheckOutcome.Pass, "Last night's backup job completed for all 12 servers.", "backup-jobs.json", new JsonObject { ["servers"] = 12, ["succeeded"] = 12 }),
        ("backup.restore_tested", CheckOutcome.Pass, "A test restore completed this quarter.", "restore-test.json", new JsonObject { ["restores"] = 1, ["succeeded"] = 1 }),
    ];

    public string Version => "1.0.0";

    public Task<Result<CollectorOutput>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tenant = context.Settings.GetValueOrDefault("tenant", "example.com");

        var checks = Result.Combine(Findings.Select(f => CheckId.Create(f.CheckId)
            .Bind(id => CheckResult.Create(id, f.Outcome, f.Observation, [f.File], context.Now, context.Name))));
        var files = Findings.Select(f => new CollectedFile(f.File, "application/json", Encoding.UTF8.GetBytes(Content(tenant, f.CheckId, f.Detail))));
        ControlStatus[] controls = [new("mfa", ControlState.InProgress), new("backups", ControlState.Active)];

        return Task.FromResult(checks.Map(c => new CollectorOutput(c, [.. files], controls)));
    }

    private static string Content(string tenant, string checkId, JsonObject detail) =>
        new JsonObject { ["tenant"] = tenant, ["check"] = checkId, ["detail"] = detail.DeepClone() }.ToJsonString();
}
