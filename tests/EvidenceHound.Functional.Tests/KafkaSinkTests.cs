using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Confluent.Kafka;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Transport.Kafka;
using Testcontainers.Redpanda;

namespace EvidenceHound.Functional.Tests;

[Trait("Category", "Functional")]
public sealed class KafkaSinkTests : IAsyncLifetime
{
    private static readonly byte[] MfaFile = Encoding.UTF8.GetBytes("{\"admins_with_mfa\": 4}");

    private readonly RedpandaContainer _kafka = new RedpandaBuilder("redpandadata/redpanda:v24.3.1").Build();

    public async ValueTask InitializeAsync() => await _kafka.StartAsync();

    public ValueTask DisposeAsync() => _kafka.DisposeAsync();

    [Fact]
    public async Task Writes_dispatcher_json_to_observation_topic()
    {
        var ct = TestContext.Current.CancellationToken;
        var evidence = Evidence.Create("ev-0001", "mfa-admins.json", "application/json", Convert.ToHexStringLower(SHA256.HashData(MfaFile)), MfaFile.Length, DateTimeOffset.UnixEpoch)
            .Match(e => e, p => throw new InvalidOperationException(p[0].Message));
        AuditUpdate[] plan =
        [
            new AuditUpdate.UploadEvidence(evidence),
            new AuditUpdate.UpdateRequirementAssessment("EXAMPLE-BASELINE-2026", "EX-1.1", ComplianceResult.Compliant, "All admins use MFA.", ["ev-0001"]),
            new AuditUpdate.UpdateAppliedControl("AC-MFA", ControlState.Active),
        ];

        PublishReport report;
        using (var sink = new KafkaSink(new KafkaSinkOptions(_kafka.GetBootstrapAddress()), new Files(evidence.Id, MfaFile)))
        {
            report = await sink.SendAsync(plan, ct);
        }

        report.Sent.ShouldBe(3);
        var messages = Consume("observation", 3);
        messages.Select(m => m["message_type"]!.GetValue<string>()).ShouldBe(["upload_attachment", "update_requirement_assessment", "update_applied_control"]);
        Convert.FromBase64String(messages[0]["values"]!["file_content"]!.GetValue<string>()).ShouldBe(MfaFile);
        messages[1]["selector"]!["requirement__ref_id"]!.GetValue<string>().ShouldBe("EX-1.1");
        messages[1]["values"]!["result"]!.GetValue<string>().ShouldBe("compliant");
        messages[1]["values"]!.AsObject().ContainsKey("evidences").ShouldBeFalse("the dispatcher sends values straight to PATCH, which wants evidence IDs, not ref_ids");
        messages[2]["values"]!["status"]!.GetValue<string>().ShouldBe("active");
    }

    [Fact]
    public async Task Evidence_that_cannot_be_read_fails_without_writing()
    {
        var evidence = Evidence.Create("ev-0002", "missing.json", "application/json", new string('a', 64), 1, DateTimeOffset.UnixEpoch)
            .Match(e => e, p => throw new InvalidOperationException(p[0].Message));

        using var sink = new KafkaSink(new KafkaSinkOptions(_kafka.GetBootstrapAddress()), new Files("other", MfaFile));
        var report = await sink.SendAsync([new AuditUpdate.UploadEvidence(evidence)], TestContext.Current.CancellationToken);

        report.Items.ShouldHaveSingleItem().Problems.Select(p => p.Code).ShouldBe(["evidence.file_not_found"]);
    }

    private List<JsonNode> Consume(string topic, int count)
    {
        using var consumer = new ConsumerBuilder<Ignore, string>(new ConsumerConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            GroupId = "evidence-hound-tests",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(topic);
        var messages = new List<JsonNode>();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (messages.Count < count && DateTime.UtcNow < deadline)
        {
            if (consumer.Consume(TimeSpan.FromSeconds(1)) is { } result)
            {
                messages.Add(JsonNode.Parse(result.Message.Value)!);
            }
        }

        return messages;
    }

    private sealed class Files(string id, byte[] content) : IEvidenceFiles
    {
        public Task<Result<ReadOnlyMemory<byte>>> ReadAsync(Evidence evidence, CancellationToken cancellationToken) =>
            Task.FromResult(evidence.Id == id
                ? Result.Ok<ReadOnlyMemory<byte>>(content)
                : Result.Fail<ReadOnlyMemory<byte>>(new Problem("evidence.file_not_found", $"No file for {evidence.Id}.")));
    }
}
