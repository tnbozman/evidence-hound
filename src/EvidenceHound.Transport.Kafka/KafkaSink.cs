using System.Text.Json.Nodes;
using Confluent.Kafka;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Transport.Kafka;

/// <summary>
/// Writes audit updates as CISO Assistant dispatcher messages to Kafka, for the official dispatcher to apply.
/// Requirement messages leave out <c>evidences</c>: the dispatcher sends values straight to the API, which
/// wants evidence IDs, and only the REST transport can look those up.
/// </summary>
public sealed class KafkaSink(KafkaSinkOptions options, IEvidenceFiles files) : IAuditUpdateSink, IDisposable
{
    private readonly IProducer<Null, string> _producer =
        new ProducerBuilder<Null, string>(new ProducerConfig { BootstrapServers = options.BootstrapServers, EnableIdempotence = true }).Build();

    public async Task<PublishReport> SendAsync(IReadOnlyList<AuditUpdate> updates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var items = new List<PublishItem>(updates.Count);
        foreach (var update in updates)
        {
            var message = await MessageAsync(update, cancellationToken);
            var outcome = await message.Match(json => ProduceAsync(json, cancellationToken), problems => Task.FromResult(Result.Fail<Unit>(problems)));
            items.Add(outcome.Match(
                _ => new PublishItem(update, UpdateDescriptions.Describe(update), PublishOutcome.Sent, []),
                problems => new PublishItem(update, UpdateDescriptions.Describe(update), PublishOutcome.Failed, problems)));
        }

        _producer.Flush(cancellationToken);
        return new PublishReport(items);
    }

    public void Dispose() => _producer.Dispose();

    private async Task<Result<JsonObject>> MessageAsync(AuditUpdate update, CancellationToken cancellationToken) =>
        await update.Match(
            async upload => (await EvidenceContent.ReadVerifiedAsync(files, upload.Evidence, cancellationToken))
                .Map(content => DispatcherMessages.ToJson(update, _ => Convert.ToBase64String(content.Span))),
            requirement => Task.FromResult(Result.Ok(WithoutEvidenceRefs(DispatcherMessages.ToJson(update, NoFile)))),
            control => Task.FromResult(Result.Ok(DispatcherMessages.ToJson(update, NoFile))));

    private async Task<Result<Unit>> ProduceAsync(JsonObject message, CancellationToken cancellationToken)
    {
        try
        {
            await _producer.ProduceAsync(options.Topic, new Message<Null, string> { Value = message.ToJsonString() }, cancellationToken);
            return Result.Ok(Unit.Value);
        }
        catch (ProduceException<Null, string> ex)
        {
            return Result.Fail<Unit>(new Problem("kafka.produce_failed", $"Kafka refused the message for topic {options.Topic}: {ex.Error.Reason}"));
        }
    }

    private static JsonObject WithoutEvidenceRefs(JsonObject message)
    {
        message["values"]!.AsObject().Remove("evidences");
        return message;
    }

    private static string NoFile(Core.Checks.Evidence evidence) => throw new InvalidOperationException($"Only uploads carry a file ({evidence.Id}).");
}

/// <summary>Where the dispatcher's Kafka is. The dispatcher reads the <c>observation</c> topic.</summary>
public sealed record KafkaSinkOptions(string BootstrapServers, string Topic = "observation");
