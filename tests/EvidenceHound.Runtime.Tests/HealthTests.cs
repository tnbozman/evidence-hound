using System.Security.Cryptography;
using System.Text;
using EvidenceHound.Runtime.Status;

namespace EvidenceHound.Runtime.Tests;

public class HealthTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);

    private static readonly RunRecord Record = new("example-idp", Start, Start.AddSeconds(5), CollectorOutcome.Succeeded, 3, 1, 0, [], new PublishCounts(0, 6, 2, 0));

    [Fact]
    public void Health_evidence_holds_the_run_record_and_links_the_control()
    {
        var health = Health.Plan(Record, "AC-AUTO");

        health.File.FileName.ShouldBe("collector-health-example-idp.json");
        health.File.MediaType.ShouldBe("application/json");
        Encoding.UTF8.GetString(health.File.Content.Span).ShouldBe(RunRecords.ToJson(Record));
        health.Upload.Evidence.Id.ShouldBe("hound-health/example-idp");
        health.Upload.Evidence.FileName.ShouldBe(health.File.FileName);
        health.Upload.Evidence.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(health.File.Content.Span)));
        health.Upload.Evidence.SizeBytes.ShouldBe(health.File.Content.Length);
        health.Upload.Evidence.CollectedAt.ShouldBe(Record.EndedAt);
        health.Upload.AppliedControlRefIds.ShouldBe(["AC-AUTO"]);
    }

    [Fact]
    public void Without_a_health_control_the_evidence_is_not_linked()
    {
        Health.Plan(Record, null).Upload.AppliedControlRefIds.ShouldBeEmpty();
    }
}
