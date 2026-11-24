using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime.Configuration;

namespace EvidenceHound.Runtime.Collectors;

/// <summary>
/// The built-in <c>ai-register</c> collector (s11-13): reads the AI register entries (the <c>ai</c> blocks of systems) in the compliance
/// repo's <c>scope/setup.yaml</c>, checks them, and saves the register as one evidence file. Its target is the repo, not CISO Assistant.
/// </summary>
public sealed class AiRegisterCollector : ICollector
{
    public const string ScopeSetting = "scope";

    private static readonly string EvidenceFile = "ai-register.json";
    private static readonly string SetupFile = "scope/setup.yaml";

    public static IReadOnlyList<string> Checks => AiRegisterChecks.CheckIds;

    public string Type => CollectorTypes.AiRegister;

    public string Version => "1.0.0";

    public CollectorDescription Describe() => new(
        Checks,
        [new(ScopeSetting, "The compliance repo's scope folder, which holds setup.yaml. A relative path is resolved against the config file's folder.", Required: true, IsPath: true)],
        []);

    public async Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var folder = context.Settings.Find(ScopeSetting) ?? "";
        var path = Path.Combine(folder, "setup.yaml");
        if (!File.Exists(path))
        {
            return Result.Fail<CollectionResult>(new Problem(
                "ai_register.setup_missing", $"No setup.yaml in '{folder}'. Check the '{ScopeSetting}' setting names the compliance repo's scope folder."));
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var parsed = ScopeLoader.Parse(new ScopeSource(SetupFile, Encoding.UTF8.GetString(bytes).TrimStart('﻿')), null, []);
        var errors = parsed.Problems.Where(p => !p.Warning).ToArray();
        if (errors.Length > 0)
        {
            return Result.Fail<CollectionResult>(new Problem(
                "ai_register.scope_invalid",
                $"{SetupFile} does not load, so the AI register cannot be checked: "
                + string.Join("; ", errors.Take(3).Select(p => $"line {p.At.Line}: {p.Message}"))
                + ". Run 'hound scope validate' and fix it."));
        }

        var now = context.Clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var entries = AiRegisterChecks.Entries(parsed.Scope.Setup);
        var evidence = CollectedEvidence.FromBytes(EvidenceFile, "application/json", Encoding.UTF8.GetBytes(Register(entries, bytes, today)));
        var checks = Result.Combine(AiRegisterChecks.Assess(entries, today).Select(f => CheckId.Create(f.CheckId)
            .Bind(id => CheckResult.Create(id, f.Outcome, f.Observation, [EvidenceFile], now, context.Name))));
        return checks.Map(c => new CollectionResult(c, [evidence], []));
    }

    /// <summary>The register as checked: each AI system's entry, with the SHA-256 of the file it came from.</summary>
    private static string Register(IReadOnlyList<AiRegisterEntry> entries, byte[] setup, DateOnly today) =>
        new JsonObject
        {
            ["file"] = SetupFile,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(setup)),
            ["systems"] = new JsonArray([.. entries.Select(e => (JsonNode)new JsonObject
            {
                ["name"] = e.System,
                ["owner"] = e.Owner,
                ["tier"] = Snake(e.Ai.Tier),
                ["use"] = Snake(e.Ai.Use),
                ["oversight"] = Snake(e.Ai.Oversight),
                ["oversight_note"] = e.Ai.OversightNote,
                ["screened_on"] = Day(e.Ai.ScreenedOn),
                ["impact_assessment"] = e.Ai.ImpactAssessment is { } impact ? Snake(impact) : null,
                ["impact_assessed_on"] = Day(e.Ai.ImpactAssessedOn),
                ["in_use_from"] = Day(e.Ai.InUseFrom),
                ["last_tested_on"] = Day(e.Ai.LastTestedOn),
                ["retired_on"] = Day(e.Ai.RetiredOn),
                ["in_use"] = AiRegisterChecks.IsInUse(e.Ai, today),
            })]),
        }.ToJsonString();

    private static string Snake<T>(T value)
        where T : struct, Enum =>
        AiRegister.Code(value).Replace('-', '_');

    private static string? Day(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
