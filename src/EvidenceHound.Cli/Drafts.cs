using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Onboarding.Workbooks;

namespace EvidenceHound.Cli;

/// <summary>
/// Where an agent's drafts wait for a person, outside the scope so nothing reaches a plan before <c>hound approve</c>:
/// <c>onboarding/drafts/audits/&lt;audit&gt;.yaml</c> and <c>onboarding/drafts/imports/&lt;audit&gt;.json</c>.
/// </summary>
internal static class Drafts
{
    public const string Audits = "onboarding/drafts/audits";
    public const string Imports = "onboarding/drafts/imports";

    public static string AuditFile(string refId) => $"{Audits}/{refId.ToLowerInvariant()}.yaml";

    public static string ImportFile(string auditRef) => $"{Imports}/{auditRef.ToLowerInvariant()}.json";

    /// <summary>Every audit file under <paramref name="folder"/> (relative to the repo) that parses, with its path and text.</summary>
    public static async Task<IReadOnlyList<(string File, string Text, ScopeAudit Audit)>> AuditsAsync(string repo, string folder, CancellationToken cancellationToken)
    {
        var path = Path.Combine(repo, folder);
        var found = new List<(string, string, ScopeAudit)>();
        foreach (var file in Directory.Exists(path) ? Directory.GetFiles(path, "*.yaml").Order(StringComparer.Ordinal).ToArray() : [])
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            if (ScopeLoader.Parse(new ScopeSource("setup.yaml", "organisation:\n  root_folder: Global\n"), null, [new ScopeSource(file, text)]).Scope.Audits is [var audit, ..])
            {
                found.Add((file, text, audit));
            }
        }

        return found;
    }

    /// <summary>The rows of a workbook that wait for a person's approval, for one audit.</summary>
    public sealed record ImportDraft(string Audit, string Workbook, IReadOnlyList<ImportedRow> Rows)
    {
        public static ImportDraft? Read(string json)
        {
            try
            {
                return JsonNode.Parse(json) is JsonObject root && root["audit"]?.GetValue<string>() is { } audit && root["workbook"]?.GetValue<string>() is { } workbook
                    ? new ImportDraft(audit, workbook, [.. (root["rows"] as JsonArray ?? []).OfType<JsonObject>().Select(r => new ImportedRow(
                        r["row"]!.GetValue<int>(), Text(r, "requirement"), Text(r, "result"), Text(r, "observation"), Text(r, "evidence"), "", ""))])
                    : null;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or NullReferenceException or FormatException)
            {
                return null;
            }
        }

        public string Write() => new JsonObject
        {
            ["audit"] = Audit,
            ["workbook"] = Workbook,
            ["drafted_by"] = OnboardAgentCommand.DraftedBy,
            ["rows"] = new JsonArray([.. Rows.Select(r => (JsonNode)new JsonObject
            {
                ["row"] = r.Row, ["requirement"] = r.Requirement, ["result"] = r.Result, ["observation"] = r.Observation, ["evidence"] = r.Evidence,
            })]),
        }.ToJsonString(CommandJson.Readable) + "\n";

        private static string Text(JsonObject row, string key) => row[key]?.GetValue<string>() ?? "";
    }
}
