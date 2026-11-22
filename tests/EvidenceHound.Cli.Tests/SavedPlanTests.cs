using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Cli.Tests;

/// <summary>s08-07: a saved plan file keeps everything <c>hound scope apply &lt;file&gt;</c> needs to apply exactly what was reviewed.</summary>
public sealed class SavedPlanTests
{
    [Fact]
    public void Round_trips_changes_drift_and_fingerprints()
    {
        var control = new ScopeObject(ScopeKind.Control, "AC-MFA", new Dictionary<string, string> { ["name"] = "Multi-factor authentication", ["domain"] = "Example Pty Ltd" })
        {
            Initial = new Dictionary<string, string> { ["status"] = "to_do" },
        };
        var document = new ScopeObject(ScopeKind.Document, "identity-policy", new Dictionary<string, string> { ["hashes"] = new string('a', 64) })
        {
            Files = [new ScopeFile("documents/identity-policy.pdf", new string('a', 64))],
        };
        var plan = new ScopePlan(
            [
                new ScopeChange(ScopeKind.Control, "AC-MFA", ScopeAction.Create, [new FieldChange("name", null, "Multi-factor authentication")], "state-1") { Desired = control },
                new ScopeChange(ScopeKind.Document, "identity-policy", ScopeAction.Update, [new FieldChange("hashes", null, new string('a', 64))], "state-2") { Desired = document },
                new ScopeChange(ScopeKind.System, "Example tablets", ScopeAction.Delete, [], null),
            ],
            [new ScopeDrift(ScopeKind.Domain, "Example Pty Ltd", DriftReason.Edited, Managed: true, [new FieldChange("description", "old", "new")])]);
        var saved = new SavedPlan(
            "https://ciso.example.com/api/",
            "2026-11-22",
            "/repo/scope",
            "/repo/hound.yaml",
            new string('b', 64),
            "Global",
            plan,
            [new AuditImpact("EX-E8-2026", [new ImpactGroup("2", "Maturity Level Two", 41, 42, 107)], [new ImpactRow("ISM-1486", "Web browsers do not process Java", "UAH", "not_assessed", "compliant", "the unlisted rule", false)])],
            new Dictionary<string, string?> { ["control/AC-MFA"] = null, ["document/identity-policy"] = "fingerprint-1", ["system/Example tablets"] = "fingerprint-2" });

        var json = SavedPlanFile.Write(saved);
        var read = SavedPlanFile.Read(json, "plan.json").Match(p => p, p => throw new ShouldAssertException(p[0].Message));

        SavedPlanFile.Write(read).ShouldBe(json);
        read.CisoAssistantUrl.ShouldBe(saved.CisoAssistantUrl);
        read.InputsSha256.ShouldBe(saved.InputsSha256);
        read.Plan.Changes.Select(c => (c.Kind, c.Key, c.Action, c.State)).ShouldBe(plan.Changes.Select(c => (c.Kind, c.Key, c.Action, c.State)));
        read.Plan.Changes[0].Desired!.Initial["status"].ShouldBe("to_do");
        read.Plan.Changes[1].Desired!.Files.ShouldBe([new ScopeFile("documents/identity-policy.pdf", new string('a', 64))]);
        read.Plan.Changes[2].Desired.ShouldBeNull();
        read.Plan.Drift.ShouldHaveSingleItem().Reason.ShouldBe(DriftReason.Edited);
        read.Read.ShouldBe(saved.Read, ignoreOrder: true);
    }

    [Fact]
    public void A_file_that_is_not_a_saved_plan_is_a_problem()
    {
        SavedPlanFile.Read("{\"changes\": []}", "plan.json").ShouldBeOfType<Result<SavedPlan>.Fail>().Errors[0].Code.ShouldBe("scope.plan_invalid");
    }
}
