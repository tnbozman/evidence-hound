using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Configuration;

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

    public string Type => CollectorTypes.Fake;

    public string Version => "1.0.0";

    public CollectorDescription Describe() => new(
        [.. Findings.Select(f => f.CheckId)],
        [
            new("tenant", "Tenant domain named in the evidence. Default: example.com.", Required: false),
            new("fail", "Check ids to report as failed instead, comma-separated, such as backup.restore_tested. Default: none.", Required: false),
        ],
        []);

    public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = context.Settings.GetOrDefault("tenant", "example.com");
        var now = context.Clock.UtcNow;

        var failing = context.Settings.GetOrDefault("fail", "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var checks = Result.Combine(Findings.Select(f => CheckId.Create(f.CheckId)
            .Bind(id => failing.Contains(f.CheckId)
                ? CheckResult.Create(id, CheckOutcome.Fail, $"The check failed (the fake collector's 'fail' setting names {f.CheckId}).", [f.File], now, context.Name)
                : CheckResult.Create(id, f.Outcome, f.Observation, [f.File], now, context.Name))));
        var files = Findings.Select(f => CollectedEvidence.FromBytes(f.File, "application/json", Encoding.UTF8.GetBytes(Content(tenant, f.CheckId, f.Detail))));
        ControlStatus[] controls = [new("mfa", ControlState.InProgress), new("backups", ControlState.Active)];

        return Task.FromResult(checks.Map(c => new CollectionResult(c, [.. files], controls)));
    }

    private static string Content(string tenant, string checkId, JsonObject detail) =>
        new JsonObject { ["tenant"] = tenant, ["check"] = checkId, ["detail"] = detail.DeepClone() }.ToJsonString();
}
