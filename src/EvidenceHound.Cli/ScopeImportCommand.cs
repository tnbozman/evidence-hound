using System.CommandLine;
using System.Text.Json.Nodes;
using EvidenceHound.Cli.Setup;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Workbooks;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound scope import</c>: an existing audit workbook's results, written to an audit's hand-written assessments. A row the workbook says
/// someone approved keeps that approver, date and row number in its observation; a row without an approver waits as a draft for
/// <c>hound approve</c>. Nothing is approved here, and nothing reaches CISO Assistant.
/// </summary>
internal static class ScopeImportCommand
{
    public static Command Build(TextWriter output, ILogger logger)
    {
        var workbook = new Argument<FileInfo>("workbook") { Description = "The audit workbook: an .xlsx or .csv file. It is read where it is and never copied into the repo." };
        var repo = new Option<DirectoryInfo>("--repo") { Description = "The config repo to work in. Default: the current folder." };
        var audit = new Option<string>("--audit") { Description = "The ref_id of the audit in scope/audits/ the rows belong to.", Required = true };
        var sheet = new Option<string?>("--sheet") { Description = "The sheet to read, by name. Default: the first." };
        var columns = new Option<string[]>("--column")
        {
            Description = $"Which heading holds what, as <name>=<heading>. Names: {string.Join(", ", WorkbookImport.Columns.Select(c => c.Name))}. Repeat for more.",
            AllowMultipleArgumentsPerToken = false,
        };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, for an agent." };
        var command = new Command("import", "Import an existing audit workbook's results into an audit, keeping each row's approver and row number. Rows without an approver wait for 'hound approve'.")
        {
            workbook, repo, audit, sheet, columns, json,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var named = (parseResult.GetValue(columns) ?? []).Where(c => c.Contains('=', StringComparison.Ordinal))
                .ToDictionary(c => c.Split('=', 2)[0].Trim(), c => c.Split('=', 2)[1], StringComparer.Ordinal);
            var imported = await ImportAsync(root, parseResult.GetRequiredValue(workbook), parseResult.GetRequiredValue(audit), parseResult.GetValue(sheet), named, cancellationToken);
            return await imported.Match(
                async report =>
                {
                    await output.WriteLineAsync((parseResult.GetValue(json) ? report.Json().ToJsonString(OnboardAgentCommand.Indented) : report.Text()).AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return command;
    }

    /// <summary>Adds <paramref name="row"/> to the audit's hand-written assessments, with its provenance in the observation.</summary>
    public static Result<string> Append(string auditYaml, ImportedRow row, string workbook, string approvedBy, string approvedOn) => YamlEdit.AppendItem(auditYaml, "assessments", YamlMap.Of(
        (row.Requirement.StartsWith("urn:", StringComparison.Ordinal) ? "urn" : "ref", new YamlText(row.Requirement)),
        ("status", new YamlText("done")),
        ("result", new YamlText(row.Result)),
        ("observation", new YamlText(WorkbookImport.Observation(row, workbook, approvedBy, approvedOn)))));

    private static async Task<Result<Report>> ImportAsync(
        string repo, FileInfo workbook, string auditRef, string? sheetName, IReadOnlyDictionary<string, string> named, CancellationToken cancellationToken)
    {
        if (!workbook.Exists)
        {
            return Result.Fail<Report>(new Problem("import.workbook_missing", $"{workbook.FullName} does not exist."));
        }

        var held = (await Drafts.AuditsAsync(repo, "scope/audits", cancellationToken)).FirstOrDefault(a => string.Equals(a.Audit.RefId, auditRef, StringComparison.OrdinalIgnoreCase));
        if (held.Audit is null)
        {
            return Result.Fail<Report>(new Problem("import.audit_unknown", $"{auditRef} is not an audit in scope/audits/. Draft it with 'hound audit draft' and have it approved first."));
        }

        var parsed = await OnboardEnvironmentCommand.ParseAsync(Path.Combine(repo, "scope"), cancellationToken);
        var snapshotFile = parsed.Scope.Setup.Frameworks.FirstOrDefault(f => f.Urn == held.Audit.Framework.Value)?.Snapshot;
        if (snapshotFile is null || !File.Exists(Path.Combine(repo, "scope", snapshotFile)))
        {
            return Result.Fail<Report>(new Problem(
                "import.snapshot_missing", $"scope/setup.yaml names no snapshot for {held.Audit.Framework.Value}. Make one with 'hound framework snapshot', so each row's requirement can be checked."));
        }

        var snapshotPath = Path.Combine(repo, "scope", snapshotFile);
        var name = workbook.Name;
        var read = FrameworkSnapshotJson.Read(await File.ReadAllTextAsync(snapshotPath, cancellationToken), snapshotPath)
            .Bind(snapshot => Workbook.Read(name, File.ReadAllBytes(workbook.FullName), sheetName).Bind(sheet => WorkbookImport.Read(sheet, name, named)).Bind(rows => Result.Ok((snapshot, rows))));
        if (read is not Result<(FrameworkSnapshot Snapshot, (IReadOnlyList<ImportedRow> Rows, int NotAssessed) Rows)>.Ok { Value: var (snapshot, (rows, notAssessed)) })
        {
            return Result.Fail<Report>(((Result<(FrameworkSnapshot, (IReadOnlyList<ImportedRow>, int))>.Fail)read).Errors);
        }

        var requirements = snapshot.Nodes.SelectMany(n => new[] { n.Urn, n.RefId }).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var assessed = held.Audit.Assessments.Select(a => a.Requirement.Key.Value).ToHashSet(StringComparer.Ordinal);
        var draftFile = Path.Combine(repo, Drafts.ImportFile(held.Audit.RefId));
        var waiting = File.Exists(draftFile) ? Drafts.ImportDraft.Read(await File.ReadAllTextAsync(draftFile, cancellationToken))?.Rows.ToList() ?? [] : [];
        var report = new Report(held.Audit.RefId, name, [], [], [], [], notAssessed);
        var yaml = held.Text;
        foreach (var row in rows)
        {
            if (!requirements.Contains(row.Requirement))
            {
                report.Unmatched.Add(row);
            }
            else if (assessed.Contains(row.Requirement) || waiting.Any(w => w.Requirement == row.Requirement))
            {
                report.AlreadyThere.Add(row);
            }
            else if (!row.Approved)
            {
                waiting.Add(row);
                report.Drafts.Add(row);
            }
            else if (Append(yaml, row, name, row.ApprovedBy, row.ApprovedOn) is Result<string>.Ok { Value: var changed })
            {
                yaml = changed;
                assessed.Add(row.Requirement);
                report.Imported.Add(row);
            }
            else
            {
                return Result.Fail<Report>(new Problem("import.audit_not_editable", $"{Path.GetFileName(held.File)}: its assessments could not be added to; add row {row.Row} by hand."));
            }
        }

        if (report.Imported.Count > 0)
        {
            await File.WriteAllTextAsync(held.File, yaml, cancellationToken);
        }

        if (report.Drafts.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(draftFile)!);
            await File.WriteAllTextAsync(draftFile, new Drafts.ImportDraft(held.Audit.RefId, name, waiting).Write(), cancellationToken);
        }

        return Result.Ok(report with { Waiting = waiting.Count });
    }

    /// <summary>What an import did with each row of the workbook.</summary>
    private sealed record Report(string Audit, string Workbook, List<ImportedRow> Imported, List<ImportedRow> Drafts, List<ImportedRow> Unmatched, List<ImportedRow> AlreadyThere, int NotAssessed)
    {
        public int Waiting { get; init; }

        public JsonObject Json() => new()
        {
            ["audit"] = Audit,
            ["workbook"] = Workbook,
            ["imported"] = Rows(Imported, approver: true),
            ["drafts"] = Rows(Drafts, approver: false),
            ["unmatched"] = Rows(Unmatched, approver: false),
            ["already_assessed"] = Rows(AlreadyThere, approver: false),
            ["not_assessed"] = NotAssessed,
            ["waiting_for_approval"] = Waiting,
        };

        public string Text() =>
            $"{Audit} from {Workbook}: {Imported.Count} row(s) imported with the workbook's approver, {Drafts.Count} drafted for 'hound approve' ({Waiting} waiting in all), "
            + $"{AlreadyThere.Count} already in the audit, {NotAssessed} not assessed and left out."
            + (Unmatched.Count > 0 ? $"\nNot requirements of the audit's framework, left out: {string.Join(", ", Unmatched.Select(r => $"row {r.Row} ({r.Requirement})"))}." : "");

        private static JsonArray Rows(IEnumerable<ImportedRow> rows, bool approver) => new([.. rows.Select(r =>
        {
            var row = new JsonObject { ["row"] = r.Row, ["requirement"] = r.Requirement, ["result"] = r.Result };
            if (approver)
            {
                row["approved_by"] = r.ApprovedBy;
                row["approved_on"] = r.ApprovedOn;
            }

            return (JsonNode)row;
        })]);
    }
}
