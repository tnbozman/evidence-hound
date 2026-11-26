using System.Globalization;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Storage;
using Microsoft.Extensions.Configuration;

namespace EvidenceHound.Cli;

/// <summary>
/// The audits that run in a cycle, read from the deployment's scope folder, and the day a command treats as today. A target that names
/// such an audit publishes to the period open that day.
/// </summary>
internal static class AuditSeries
{
    /// <summary>Overrides today's date (UTC), as <c>yyyy-MM-dd</c>, for checks across a period boundary.</summary>
    public const string TodaySetting = "HOUND_TODAY";

    public static DateOnly Today(IConfiguration configuration) =>
        DateOnly.TryParseExact(configuration[TodaySetting], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var today)
            ? today
            : DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Each audit in the scope folder that declares a cycle, by ref_id; none when the deployment names no scope.</summary>
    public static async Task<IReadOnlyDictionary<string, AuditCycle>> CyclesAsync(string? scopeDir, CancellationToken cancellationToken)
    {
        var setupPath = scopeDir is null ? null : Path.Combine(scopeDir, "setup.yaml");
        if (setupPath is null || !File.Exists(setupPath))
        {
            return new Dictionary<string, AuditCycle>(StringComparer.Ordinal);
        }

        var controlsPath = Path.Combine(scopeDir!, "controls.yaml");
        var auditsDir = Path.Combine(scopeDir!, "audits");
        var audits = Directory.Exists(auditsDir) ? Directory.GetFiles(auditsDir, "*.yaml").Order(StringComparer.Ordinal).ToArray() : [];
        var parsed = ScopeLoader.Parse(
            await ReadAsync(setupPath, cancellationToken),
            File.Exists(controlsPath) ? await ReadAsync(controlsPath, cancellationToken) : null,
            [.. await Task.WhenAll(audits.Select(a => ReadAsync(a, cancellationToken)))]);
        return parsed.Scope.Audits
            .Where(a => a.Cycle is not null)
            .DistinctBy(a => a.RefId)
            .ToDictionary(a => a.RefId, a => a.Cycle!, StringComparer.Ordinal);
    }

    /// <summary>The config with each target that names a series pointed at the period open on <paramref name="today"/>.</summary>
    public static async Task<DeploymentConfig> ResolveAsync(DeploymentConfig config, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        var cycles = await CyclesAsync(config.Scope, cancellationToken);
        return cycles.Count == 0
            ? config
            : config with { Targets = [.. config.Targets.Select(t => t with { ComplianceAssessmentRefId = AuditPeriods.Target(t.ComplianceAssessmentRefId, cycles, today) })] };
    }

    /// <summary>A collect step for the deployment that keeps each ended audit period's final run for the configured years.</summary>
    public static async Task<CollectStep> CollectStepAsync(
        DeploymentConfig config, IConfiguration configuration, CollectorServices services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        var today = Today(configuration);
        var cycles = await CyclesAsync(config.Scope, cancellationToken);
        return new CollectStep(DeploymentFile.Secrets(configuration), new FileEvidenceStore(config.Evidence.Root), new SystemClock(), config.Evidence.RetentionDays, services)
        {
            PeriodEnds = [.. cycles.SelectMany(c => AuditPeriods.Until(c.Key, c.Value, today)).Where(p => p.End < today).Select(p => p.End).Distinct().Order()],
            PeriodRetentionYears = config.Evidence.PeriodRetentionYears,
            Scope = config.Scope is { } scope && File.Exists(Path.Combine(scope, "capabilities.yaml")) ? (await OnboardEnvironmentCommand.ParseAsync(scope, cancellationToken)).Scope : null,
        };
    }

    private static async Task<ScopeSource> ReadAsync(string path, CancellationToken cancellationToken) =>
        new(path, await File.ReadAllTextAsync(path, cancellationToken));
}
