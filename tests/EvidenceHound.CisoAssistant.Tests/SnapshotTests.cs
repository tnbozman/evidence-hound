using System.Text.Json.Nodes;
using EvidenceHound.Core.Reporting;
using static EvidenceHound.CisoAssistant.Tests.SupplierClientTests;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>s12-04: what a posture snapshot reads from CISO Assistant besides the audits, from fixture API responses.</summary>
public sealed class SnapshotTests : IDisposable
{
    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public SnapshotTests()
    {
        static JsonObject Ref(string id, string str) => new() { ["id"] = id, ["str"] = str };
        static JsonArray Owners(params string[] names) => [.. names.Select((n, i) => (JsonNode)Ref("u-" + i, n))];

        Respond(_ca, "GET", "/api/applied-controls/", List(
            new JsonObject { ["id"] = "ac-1", ["ref_id"] = "AC-BACKUP", ["name"] = "Daily backups", ["status"] = "in_progress", ["owner"] = Owners("Example backup owner"), ["eta"] = "2026-12-20" },
            new JsonObject { ["id"] = "ac-2", ["ref_id"] = null, ["name"] = "MFA for all users", ["status"] = "active", ["owner"] = new JsonArray(), ["eta"] = null }));
        Respond(_ca, "GET", "/api/risk-acceptances/", List(
            new JsonObject { ["id"] = "ra-1", ["ref_id"] = "RA-KIOSK", ["name"] = "Legacy kiosk", ["state"] = "accepted", ["approver"] = Ref("u-9", "Example CTO"), ["accepted_at"] = "2026-11-03T04:05:06Z", ["expiry_date"] = "2027-05-01" }));
        Respond(_ca, "GET", "/api/security-exceptions/", List(
            new JsonObject { ["id"] = "se-1", ["ref_id"] = "SE-MACRO", ["name"] = "Finance macros", ["status"] = "approved", ["approver"] = Ref("u-9", "Example CTO"), ["created_at"] = "2026-11-10T00:00:00Z", ["expiration_date"] = "2027-01-15" }));
        Respond(_ca, "GET", "/api/findings/", List(
            new JsonObject { ["id"] = "fi-1", ["ref_id"] = "FND-1", ["name"] = "Unsupported OS on two laptops", ["status"] = "in_progress", ["owner"] = Owners("Example IT lead"), ["eta"] = "2026-11-15" }));
        Respond(_ca, "GET", "/api/incidents/", List(
            new JsonObject { ["id"] = "in-1", ["ref_id"] = "INC-7", ["name"] = "Phishing email clicked", ["status"] = "closed", ["severity"] = 3, ["reported_at"] = "2026-11-05T09:00:00Z", ["updated_at"] = "2026-11-08T10:00:00Z" }));
        Respond(_ca, "GET", "/api/risk-scenarios/", List(
            new JsonObject
            {
                ["id"] = "rs-1",
                ["ref_id"] = "R-1",
                ["name"] = "Ransomware on file server",
                ["current_level"] = new JsonObject { ["name"] = "High", ["value"] = 3 },
                ["residual_level"] = new JsonObject { ["name"] = "Medium", ["value"] = 2 },
                ["owner"] = Owners("Example CTO"),
                ["treatment"] = "mitigate",
                ["eta"] = "2027-03-31",
            }));
        Respond(_ca, "GET", "/api/task-nodes/", List(
            new JsonObject { ["id"] = "tn-1", ["task_template"] = Ref("tt-1", "Backups: restore test"), ["due_date"] = "2026-11-20", ["status"] = "pending", ["assigned_to"] = Owners("Example backup owner") }));
        Respond(_ca, "GET", "/api/entity-assessments/", List(
            new JsonObject { ["id"] = "ea-1", ["name"] = "Example Cloud review 2026", ["due_date"] = "2026-12-01", ["status"] = "in_progress", ["conclusion"] = null, ["authors"] = Owners("Example GRC lead") },
            new JsonObject { ["id"] = "ea-2", ["name"] = "Example Mail review 2026", ["due_date"] = "2026-06-01", ["status"] = "done", ["conclusion"] = "ok", ["authors"] = Owners("Example GRC lead") }));
    }

    [Fact]
    public async Task Built_from_fixture_api_responses()
    {
        var records = (await _ca.Posture.ReadAsync(_ct)).Value();

        records.Controls.ShouldBe(
        [
            new ControlItem("AC-BACKUP", "Daily backups", "in_progress", "Example backup owner", new DateOnly(2026, 12, 20)) { Link = _ca.Posture.Link("applied-controls", "ac-1") },
            new ControlItem("", "MFA for all users", "active", null, null) { Link = _ca.Posture.Link("applied-controls", "ac-2") },
        ]);
        records.Acceptances.ShouldBe(
        [
            new AcceptanceItem(AcceptanceKind.RiskAcceptance, "RA-KIOSK", "Legacy kiosk", "accepted", "Example CTO", new DateOnly(2026, 11, 3), new DateOnly(2027, 5, 1)) { Link = _ca.Posture.Link("risk-acceptances", "ra-1") },
            new AcceptanceItem(AcceptanceKind.SecurityException, "SE-MACRO", "Finance macros", "approved", "Example CTO", new DateOnly(2026, 11, 10), new DateOnly(2027, 1, 15)) { Link = _ca.Posture.Link("security-exceptions", "se-1") },
        ]);
        records.Remediation.ShouldBe([new RemediationItem("FND-1", "Unsupported OS on two laptops", "Example IT lead", new DateOnly(2026, 11, 15), "in_progress", null) { Link = _ca.Posture.Link("findings", "fi-1") }]);
        records.Incidents.ShouldBe([new IncidentItem("INC-7", "Phishing email clicked", "closed", "3", new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 8)) { Link = _ca.Posture.Link("incidents", "in-1") }]);
        records.Risks.ShouldBe([new RiskItem("R-1", "Ransomware on file server", "High", "Medium", 2, "Example CTO", "mitigate", new DateOnly(2027, 3, 31)) { Link = _ca.Posture.Link("risk-scenarios", "rs-1") }]);
        records.Due.ShouldBe(
        [
            new DueItem(DueKind.ManualEvidence, "Backups: restore test", "Example backup owner", new DateOnly(2026, 11, 20), "pending") { Link = _ca.Posture.Link("task-nodes", "tn-1") },
            new DueItem(DueKind.SupplierReview, "Example Cloud review 2026", "Example GRC lead", new DateOnly(2026, 12, 1), "in_progress") { Link = _ca.Posture.Link("entity-assessments", "ea-1") },
            new DueItem(DueKind.SupplierReview, "Example Mail review 2026", "Example GRC lead", new DateOnly(2026, 6, 1), "completed") { Link = _ca.Posture.Link("entity-assessments", "ea-2") },
        ]);
    }

    [Fact]
    public void Links_point_at_the_web_app_not_the_api()
    {
        _ca.Posture.Link("incidents", "in-1").ShouldBe($"{_ca.Server.Url}/incidents/in-1");
    }

    public void Dispose() => _ca.Dispose();
}
