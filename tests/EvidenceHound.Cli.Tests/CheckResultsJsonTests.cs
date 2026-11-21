using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Planning;

namespace EvidenceHound.Cli.Tests;

public class CheckResultsJsonTests
{
    [Fact]
    public void Parses_every_field()
    {
        const string json = """
            {"checks": [{"check_id": "identity.mfa.admins", "outcome": "not_applicable", "observation": "No admins.",
              "evidence_refs": ["ev-1"], "collected_at": "2026-01-15T09:30:00+10:00", "source": "example-idp"}]}
            """;

        var check = CheckResultsJson.Parse(json, "checks.json").Match(list => list.Single(), p => throw new InvalidOperationException(p[0].Message));

        check.CheckId.Value.ShouldBe("identity.mfa.admins");
        check.Outcome.ShouldBe(CheckOutcome.NotApplicable);
        check.Observation.ShouldBe("No admins.");
        check.EvidenceRefs.ShouldBe(["ev-1"]);
        check.CollectedAt.ShouldBe(new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.FromHours(10)));
        check.Source.ShouldBe("example-idp");
    }

    [Theory]
    [InlineData("{", "checks.json_invalid")]
    [InlineData("""{"checks": [{"check_id": "Bad.ID", "outcome": "pass", "observation": "", "evidence_refs": [], "collected_at": "2026-01-15T09:30:00Z", "source": "x"}]}""", "check_id.invalid")]
    [InlineData("""{"checks": [{"check_id": "a.b", "outcome": "passed", "observation": "", "evidence_refs": [], "collected_at": "2026-01-15T09:30:00Z", "source": "x"}]}""", "checks.outcome_unknown")]
    [InlineData("""{"checks": [{"check_id": "a.b", "outcome": "pass", "observation": "", "evidence_refs": [], "collected_at": "2026-01-15T09:30:00Z", "source": ""}]}""", "check_result.source_empty")]
    [InlineData("""{"checks": [{"check_id": "a.b", "outcome": "pass"}]}""", "checks.json_invalid")]
    [InlineData("""{}""", "checks.json_invalid")]
    public void Problems_name_the_file_and_entry(string json, string code)
    {
        var problem = CheckResultsJson.Parse(json, "checks.json").Match(_ => throw new InvalidOperationException("parsed"), p => p[0]);

        problem.Code.ShouldBe(code);
        problem.Message.ShouldStartWith("checks.json");
    }

    [Fact]
    public void Sample_fixture_parses()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "fixtures", "checks-sample.json"));

        CheckResultsJson.Parse(json, "checks-sample.json").Match(list => list.Count, _ => -1).ShouldBe(9);
    }

    [Fact]
    public void Parses_evidence_and_control_statuses()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "fixtures", "checks-sample.json"));

        var run = CheckResultsJson.ParseRun(json, "checks-sample.json").Match(r => r, p => throw new InvalidOperationException(p[0].Message));

        run.Checks.Count.ShouldBe(9);
        run.Evidence.Select(e => e.Id).ShouldBe(["ev-0001", "ev-0002", "ev-0003", "ev-0004", "ev-0005"]);
        run.Evidence[0].FileName.ShouldBe("mfa-admins.json");
        run.ControlStatuses.Select(c => (c.ControlKey, c.State)).ShouldBe([("mfa", ControlState.InProgress), ("backups", ControlState.Active)]);
    }

    [Theory]
    [InlineData("""{"checks": [], "evidence": [{"id": "ev-1", "file_name": "a", "media_type": "text/plain", "sha256": "abc", "size_bytes": 1, "collected_at": "2026-01-15T09:30:00Z"}]}""", "evidence.sha256_invalid")]
    [InlineData("""{"checks": [], "control_statuses": [{"control": "mfa", "status": "done"}]}""", "checks.control_status_unknown")]
    public void Evidence_and_control_status_problems_name_the_file(string json, string code)
    {
        var problem = CheckResultsJson.ParseRun(json, "checks.json").Match(_ => throw new InvalidOperationException("parsed"), p => p[0]);

        problem.Code.ShouldBe(code);
        problem.Message.ShouldStartWith("checks.json");
    }
}
