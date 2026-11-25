using System.Globalization;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Publishing;

/// <summary>What a run knows to measure from: every collector's latest results, how many collectors it ran and how many succeeded, and when.</summary>
public sealed record RunMeasures(IReadOnlyList<CheckResult> Latest, int CollectorsRan, int CollectorsSucceeded, DateTimeOffset Now);

/// <summary>What a run did to the metrics it feeds: the samples it recorded, and what it could not measure or record.</summary>
public sealed record MetricReport(IReadOnlyList<MetricSampleWrite> Recorded, IReadOnlyList<Problem> Problems)
{
    public static MetricReport None { get; } = new([], []);
}

/// <summary>Records the metric samples of instances with <c>source: hound</c> after a run (s16-03).</summary>
public static class MetricPublisher
{
    /// <summary>
    /// Measures each run-fed instance and records the value for its current period, unless CISO Assistant already holds it. An instance
    /// CISO Assistant does not hold yet waits for <c>hound scope apply</c>; a measure with nothing to count records nothing. Both are named.
    /// </summary>
    public static async Task<MetricReport> PublishAsync(ICisoAssistantClient client, IReadOnlyList<ScopeMetricInstance> instances, RunMeasures run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(run);
        var fed = instances.Where(i => i.Source is not null).DistinctBy(i => i.RefId, StringComparer.Ordinal).ToList();
        if (fed.Count == 0)
        {
            return MetricReport.None;
        }

        var held = await client.ListMetricInstancesAsync(cancellationToken);
        if (held is not Result<IReadOnlyDictionary<string, HeldMetric>>.Ok { Value: var metrics })
        {
            return new MetricReport([], ((Result<IReadOnlyDictionary<string, HeldMetric>>.Fail)held).Errors);
        }

        var problems = new List<Problem>();
        var recorded = new List<MetricSampleWrite>();
        var audits = new Dictionary<string, Result<IReadOnlyList<CurrentRow>>>(StringComparer.Ordinal);
        foreach (var instance in fed)
        {
            var source = instance.Source!;
            var measured = await MeasureAsync(client, source, run, audits, cancellationToken);
            if (measured is not Result<double>.Ok { Value: var value })
            {
                problems.AddRange(((Result<double>.Fail)measured).Errors.Select(p => new Problem(p.Code, $"{instance.RefId}: {p.Message}")));
                continue;
            }

            if (!metrics.TryGetValue(instance.RefId, out var metric))
            {
                problems.Add(new Problem(
                    "metric.not_applied",
                    $"{instance.RefId}: CISO Assistant has no metric instance with this ref_id, so this run's value is not recorded. Run hound scope apply first."));
                continue;
            }

            var samples = await client.ListMetricSamplesAsync(metric.Id, cancellationToken);
            if (samples is not Result<IReadOnlyList<HeldSample>>.Ok { Value: var periods })
            {
                problems.AddRange(((Result<IReadOnlyList<HeldSample>>.Fail)samples).Errors);
                continue;
            }

            var why = Why(source, value);
            if (MetricSamples.Plan(new MetricMeasured(instance.RefId, metric.Id, metric.Frequency, value, why), periods, run.Now) is not { } write)
            {
                continue;
            }

            (await client.RecordMetricSampleAsync(write, run.Now, cancellationToken)).Match(
                _ =>
                {
                    recorded.Add(write);
                    return Unit.Value;
                },
                p =>
                {
                    problems.AddRange(p);
                    return Unit.Value;
                });
        }

        return new MetricReport(recorded, problems);
    }

    private static async Task<Result<double>> MeasureAsync(
        ICisoAssistantClient client, MetricSource source, RunMeasures run, Dictionary<string, Result<IReadOnlyList<CurrentRow>>> audits, CancellationToken cancellationToken)
    {
        double? value;
        switch (source.Measure)
        {
            case "requirements_compliant_pct":
                var audit = source.Audit?.Value ?? "";
                if (!audits.TryGetValue(audit, out var rows))
                {
                    rows = await client.ListRequirementAssessmentsAsync(audit, cancellationToken);
                    audits[audit] = rows;
                }

                if (rows is not Result<IReadOnlyList<CurrentRow>>.Ok { Value: var current })
                {
                    return Result.Fail<double>(((Result<IReadOnlyList<CurrentRow>>.Fail)rows).Errors);
                }

                value = MetricMeasures.RequirementsCompliantPct([.. current.Select(r => r.Result)]);
                break;
            case "evidence_freshness_pct":
                value = MetricMeasures.EvidenceFreshnessPct(source.Checks, run.Latest, run.Now);
                break;
            case "collector_success_rate":
                value = MetricMeasures.CollectorSuccessRate(run.CollectorsSucceeded, run.CollectorsRan);
                break;
            default:
                value = MetricMeasures.CheckPassRate(source.Checks, run.Latest);
                break;
        }

        return value is { } measured
            ? Result.Ok(measured)
            : Result.Fail<double>(new Problem("metric.nothing_measured", $"{source.Measure} found nothing to count{Over(source)}, so no value is recorded for this period."));
    }

    private static string Why(MetricSource source, double value) =>
        string.Create(CultureInfo.InvariantCulture, $"{source.Measure}{Over(source)} is {Math.Round(value, 2, MidpointRounding.AwayFromZero)}%.");

    private static string Over(MetricSource source) =>
        source.Audit is { } audit ? $" in audit {audit.Value}" : source.Checks.Count > 0 ? $" over {string.Join(", ", source.Checks)}" : "";
}
