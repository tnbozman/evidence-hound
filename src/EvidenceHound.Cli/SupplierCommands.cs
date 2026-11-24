using System.CommandLine;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Onboarding.Output;
using EvidenceHound.Onboarding.Suppliers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound supplier draft</c>: writes a third party's file from options, with no prompts, so an agent can draft it. The contract is a
/// draft and the review is open: a person approves the supplier and concludes its review with <c>hound approve</c>.
/// </summary>
internal static class SupplierCommands
{
    private static readonly string RefIdInvalid = "supplier.ref_id_invalid";

    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration)
    {
        var logger = loggers.CreateLogger("hound.supplier");
        var repo = new Option<DirectoryInfo>("--repo") { Description = "The config repo to work in. Default: the current folder." };
        var system = new Option<string?>("--system") { Description = "The system this supplier is drafted for, named in the file's first line." };
        static Option<string> Text(string name, string description) => new(name) { Description = description, Required = true };
        var entityRef = Text("--ref", "The supplier's ref_id, such as SUP-EXAMPLE.");
        var entityName = Text("--name", "The supplier's name.");
        var solutionRef = Text("--solution-ref", "The ref_id of what the supplier provides, such as SOL-EXAMPLE.");
        var solutionName = Text("--solution-name", "The solution's name.");
        var contractRef = Text("--contract-ref", "The contract's ref_id, such as CTR-EXAMPLE.");
        var contractName = Text("--contract-name", "The contract's name.");
        var levels = new Option<string[]>("--level")
        {
            Description = $"A level with its reason, as <name>=<value>:<reason>. Names: {string.Join(", ", SupplierProposal.Scales.Select(s => $"{s.Name} ({s.Low} to {s.High})"))}. Repeat for more.",
            AllowMultipleArgumentsPerToken = false,
        };
        var kind = new Option<string>("--kind") { Description = "What the third party is: vendor (makes the product), reseller (sells it) or msp (a managed service provider that runs it).", DefaultValueFactory = _ => "vendor" };
        kind.AcceptOnlyFromAmong([.. Kinds.Keys]);
        var standsFor = new Option<string?>("--for") { Description = "For a reseller (required) or an msp that runs one vendor's product: that vendor's ref_id, already in scope/third-parties/. An msp that runs the organisation's own systems leaves it out." };
        var documents = new Option<string[]>("--document")
        {
            Description = "A document that was read for this draft and kept in the repo, as a path relative to the repo. Named in the file with its SHA-256. Repeat for more.",
            AllowMultipleArgumentsPerToken = false,
        };
        var description = new Option<string?>("--description") { Description = "What the third party does for the organisation, in a sentence." };
        var start = new Option<string?>("--start") { Description = "The contract's start date, yyyy-MM-dd." };
        var end = new Option<string?>("--end") { Description = "The contract's end or renewal date, yyyy-MM-dd." };
        var expense = new Option<decimal?>("--annual-expense") { Description = "The contract's annual expense, a plain number." };
        var currency = new Option<string?>("--currency") { Description = "The annual expense's currency, ISO 4217 such as AUD or USD." };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, for an agent." };
        var data = new Option<string[]>("--data")
        {
            Description = "Sensitive data the solution handles, as <kind>:<role>[:<detail>], such as cui:stores:CUI//SP-CTI. Kinds: cui, pii, export_controlled, classified. Roles: stores, processes, transmits, protects. Repeat for more.",
            AllowMultipleArgumentsPerToken = false,
        };
        var draft = new Command("draft", "Draft a third party's file under scope/third-parties/ with a draft contract and an open review. A person approves it with 'hound approve'.")
        {
            repo, system, entityRef, entityName, solutionRef, solutionName, contractRef, contractName, levels, kind, standsFor, documents,
            description, start, end, expense, currency, data, json,
        };
        draft.SetAction(async (parseResult, cancellationToken) =>
        {
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var refs = Result.Combine(
            [
                RefIdPrompt.Given(RefIdKind.Supplier, parseResult.GetRequiredValue(entityRef), RefIdInvalid),
                RefIdPrompt.Given(RefIdKind.Solution, parseResult.GetRequiredValue(solutionRef), RefIdInvalid),
                RefIdPrompt.Given(RefIdKind.Contract, parseResult.GetRequiredValue(contractRef), RefIdInvalid),
            ]);
            if (refs is Result<IReadOnlyList<string>>.Fail refused)
            {
                return Log.Problems(logger, refused.Errors);
            }

            var completed = refs.Match(r => r, _ => []);
            var choice = new SupplierChoice(
                completed[0], parseResult.GetRequiredValue(entityName), completed[1],
                parseResult.GetRequiredValue(solutionName), completed[2], parseResult.GetRequiredValue(contractName));
            var role = parseResult.GetValue(kind) ?? "vendor";
            var given = new ContractTerms(
                parseResult.GetValue(start), parseResult.GetValue(end),
                parseResult.GetValue(expense)?.ToString(CultureInfo.InvariantCulture), parseResult.GetValue(currency)?.ToUpperInvariant());
            var written = await Levels(parseResult.GetValue(levels) ?? [])
                .Bind(read => Terms(given).Map(terms => (read, terms)))
                .Bind(read => Data(parseResult.GetValue(data) ?? []).Map(found => (read.read, terms: read.terms, found)))
                .BindAsync(async read => (await NotesAsync(root, role, parseResult.GetValue(standsFor), parseResult.GetValue(documents) ?? [], cancellationToken))
                    .Bind(notes => Result.Ok((read: read.read, notes: notes with { Description = parseResult.GetValue(description) ?? notes.Description, Terms = read.terms, Data = read.found }))))
                .BindAsync(read => DraftAsync(root, choice, read.read, read.notes, parseResult.GetValue(system), AuditSeries.Today(configuration), cancellationToken));
            return await written.Match(
                async file =>
                {
                    await terminal.Output.WriteLineAsync((parseResult.GetValue(json)
                        ? new JsonObject { ["file"] = file, ["supplier"] = choice.EntityRef, ["kind"] = role, ["approved"] = false }.ToJsonString(OnboardAgentCommand.Indented)
                        : $"Drafted {file} with a draft contract. A person approves {choice.EntityRef} and concludes its review with 'hound approve'.").AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return new Command("supplier", "Work with third parties in a compliance repo.") { draft, List(terminal, logger, repo, json) };
    }

    /// <summary><c>hound supplier list --data &lt;kind&gt;</c>: each supplier's solution handling a kind of sensitive data, with its authority.</summary>
    private static Command List(Terminal terminal, ILogger logger, Option<DirectoryInfo> repo, Option<bool> json)
    {
        var data = new Option<string>("--data") { Description = "The kind of sensitive data: cui, pii, export_controlled or classified.", Required = true };
        var list = new Command("list", "List the suppliers and solutions that handle a kind of sensitive data, with what authorises each: the list a prime or auditor asks for.")
        {
            repo, data, json,
        };
        list.SetAction(async (parseResult, cancellationToken) =>
        {
            var code = parseResult.GetRequiredValue(data);
            if (SensitiveDataList.Kind(code) is not { } kind)
            {
                return Log.Problems(logger, [new Problem("data.kind_invalid", $"'{code}' is not a kind of sensitive data; use cui, pii, export_controlled or classified.")]);
            }

            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var parsed = await ScopeAsync(root, cancellationToken);
            if (parsed.Problems.Count > 0)
            {
                return Log.Problems(logger, parsed.Problems.Select(p => new Problem(p.Code, $"{p.At.File}:{p.At.Line}: {p.Message}")));
            }

            var holders = SensitiveDataList.Handling(parsed.Scope, kind);
            var text = parseResult.GetValue(json) ? Json(code, holders) : Markdown(code, holders);
            await terminal.Output.WriteLineAsync(text.AsMemory(), cancellationToken);
            return ExitCodes.Ok;
        });
        return list;
    }

    private static async Task<ScopeParse> ScopeAsync(string repo, CancellationToken cancellationToken)
    {
        var scope = Path.Combine(repo, "scope");
        var setup = Path.Combine(scope, "setup.yaml");
        var thirdParties = Path.Combine(scope, "third-parties");
        var files = Directory.Exists(thirdParties) ? Directory.GetFiles(thirdParties, "*.yaml").Order(StringComparer.Ordinal).ToArray() : [];
        return ScopeLoader.Parse(
            new ScopeSource(setup, File.Exists(setup) ? await File.ReadAllTextAsync(setup, cancellationToken) : ""),
            null,
            [],
            thirdParties: [.. await Task.WhenAll(files.Select(async f => new ScopeSource(Path.GetRelativePath(repo, f).Replace('\\', '/'), await File.ReadAllTextAsync(f, cancellationToken))))]);
    }

    private static string Json(string kind, IReadOnlyList<SensitiveDataHolder> holders) => new JsonObject
    {
        ["data"] = kind,
        ["solutions"] = new JsonArray([.. holders.Select(h => (JsonNode)new JsonObject
        {
            ["supplier"] = h.Supplier,
            ["supplier_name"] = h.SupplierName,
            ["solution"] = h.Solution,
            ["solution_name"] = h.SolutionName,
            ["role"] = Role(h.Data),
            ["detail"] = h.Data.Detail,
            ["authority"] = h.Data.Authority?.Name,
            ["evidence"] = h.Data.Authority?.Evidence,
            ["expires"] = Day(h.Data.Authority?.Expires),
            ["systems"] = new JsonArray([.. h.Systems.Select(s => (JsonNode)s)]),
        })]),
    }.ToJsonString(OnboardAgentCommand.Indented);

    private static string Markdown(string kind, IReadOnlyList<SensitiveDataHolder> holders)
    {
        static string Cell(string? text) => string.IsNullOrEmpty(text) ? "" : text.Replace("|", "\\|", StringComparison.Ordinal);
        var lines = new List<string>
        {
            $"# Suppliers handling {kind}",
            "",
            holders.Count == 0 ? $"No supplier's solution lists {kind} in its data." : "| Supplier | Solution | Role | Detail | Authority | Evidence | Expires | Systems |",
        };
        if (holders.Count > 0)
        {
            lines.Add("| --- | --- | --- | --- | --- | --- | --- | --- |");
            lines.AddRange(holders.Select(h =>
                $"| {h.Supplier} | {h.Solution} | {Role(h.Data)} | {Cell(h.Data.Detail)} | {Cell(h.Data.Authority?.Name ?? "no authority recorded")} | {Cell(h.Data.Authority?.Evidence)} | {Day(h.Data.Authority?.Expires)} | {Cell(string.Join(", ", h.Systems))} |"));
        }

        return string.Join('\n', lines);
    }

    private static string Role(SensitiveData data) => data.Role.ToString().ToLowerInvariant();

    private static string? Day(DateOnly? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Each <c>name=value:reason</c> as a level within its scale, with a reason. Every problem is reported. Pure.</summary>
    internal static Result<IReadOnlyList<LevelProposal>> Levels(IReadOnlyList<string> given)
    {
        var problems = new List<string>();
        var levels = new List<LevelProposal>();
        foreach (var text in given)
        {
            var name = text.Split('=', 2)[0].Trim();
            var rest = text.Contains('=', StringComparison.Ordinal) ? text.Split('=', 2)[1] : "";
            var reason = rest.Contains(':', StringComparison.Ordinal) ? rest.Split(':', 2)[1].Trim() : "";
            var scale = SupplierProposal.Scales.FirstOrDefault(s => s.Name == name);
            if (scale.Name is null)
            {
                problems.Add($"'{name}' is not a level; use one of {string.Join(", ", SupplierProposal.Scales.Select(s => s.Name))}");
            }
            else if (!int.TryParse(rest.Split(':', 2)[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                problems.Add($"{name} has no whole number; write it as {name}=<value>:<reason>");
            }
            else if (value < scale.Low || value > scale.High)
            {
                problems.Add($"{name} is {value}, outside {scale.Low} to {scale.High}");
            }
            else if (reason.Length == 0)
            {
                problems.Add($"{name} has no reason; write it as {name}={value}:<reason>");
            }
            else if (levels.Any(l => l.Name == name))
            {
                problems.Add($"{name} is given more than once");
            }
            else
            {
                levels.Add(new LevelProposal(name, value, reason, [OnboardAgentCommand.DraftedBy]));
            }
        }

        return problems.Count > 0
            ? Result.Fail<IReadOnlyList<LevelProposal>>([.. problems.Select(p => new Problem("supplier.level_invalid", $"{p}."))])
            : Result.Ok<IReadOnlyList<LevelProposal>>(levels);
    }

    private static readonly string[] DataRoles = ["stores", "processes", "transmits", "protects"];

    /// <summary>Each <c>kind:role[:detail]</c> as sensitive data; classified and export-controlled data name which. Every problem is reported. Pure.</summary>
    internal static Result<IReadOnlyList<DraftedData>> Data(IReadOnlyList<string> given)
    {
        var problems = new List<Problem>();
        var found = new List<DraftedData>();
        foreach (var text in given)
        {
            var parts = text.Split(':', 3);
            var (kind, role, detail) = (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "", parts.Length > 2 ? parts[2].Trim() : null);
            if (SensitiveDataList.Kind(kind) is not { } known)
            {
                problems.Add(new Problem("data.kind_invalid", $"'{kind}' is not a kind of sensitive data; use cui, pii, export_controlled or classified."));
            }
            else if (!DataRoles.Contains(role))
            {
                problems.Add(new Problem("data.role_invalid", $"--data {text} has no role; write it as {kind}:<role>, with stores, processes, transmits or protects."));
            }
            else if (known is SensitiveDataKind.Classified or SensitiveDataKind.ExportControlled && string.IsNullOrEmpty(detail))
            {
                problems.Add(new Problem("data.detail_missing", $"{kind} needs a detail, such as ITAR, EAR, DSGL or PROTECTED: write it as {kind}:{role}:<detail>."));
            }
            else
            {
                found.Add(new DraftedData(kind, role, string.IsNullOrEmpty(detail) ? null : detail));
            }
        }

        return problems.Count > 0 ? Result.Fail<IReadOnlyList<DraftedData>>(problems) : Result.Ok<IReadOnlyList<DraftedData>>(found);
    }

    /// <summary>The contract's dates are yyyy-MM-dd and the end is not before the start. Pure.</summary>
    internal static Result<ContractTerms> Terms(ContractTerms terms)
    {
        var problems = new List<Problem>();
        DateOnly? Day(string name, string? text)
        {
            if (text is null)
            {
                return null;
            }

            if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                return day;
            }

            problems.Add(new Problem("supplier.contract_date_invalid", $"--{name} '{text}' is not a date; write it as yyyy-MM-dd."));
            return null;
        }

        var (from, to) = (Day("start", terms.Start), Day("end", terms.End));
        if (from is { } first && to is { } last && last < first)
        {
            problems.Add(new Problem("supplier.contract_dates_invalid", $"The contract ends before it starts: --end {terms.End} is before --start {terms.Start}."));
        }

        return problems.Count > 0 ? Result.Fail<ContractTerms>(problems) : Result.Ok(terms);
    }

    /// <summary>The words that say what a reseller or a managed service provider is to the vendor it stands for.</summary>
    private static readonly Dictionary<string, string?> Kinds = new(StringComparer.Ordinal)
    {
        ["vendor"] = null,
        ["reseller"] = "Reseller of",
        ["msp"] = "Managed service provider for",
    };

    /// <summary>
    /// The draft's description and kept documents: a reseller or managed service provider names a vendor already in the repo, and every
    /// document is a file in the repo, named with its SHA-256.
    /// </summary>
    private static async Task<Result<SupplierNotes>> NotesAsync(string repo, string kind, string? standsFor, IReadOnlyList<string> documents, CancellationToken cancellationToken)
    {
        var problems = new List<Problem>();
        string? description = null;
        if (Kinds[kind] is { } words)
        {
            var vendor = standsFor is null ? null : (await OnboardSystemCommand.ThirdPartiesAsync(repo, cancellationToken)).FirstOrDefault(e => e.RefId == standsFor);
            if (standsFor is null && kind == "msp")
            {
                description = "Managed service provider.";
            }
            else if (standsFor is null)
            {
                problems.Add(new Problem("supplier.for_missing", $"A {kind} names the vendor with --for: the ref_id of the third party whose product it sells or runs."));
            }
            else if (vendor is null)
            {
                problems.Add(new Problem("supplier.for_unknown", $"{standsFor} is not a third party in scope/third-parties/. Draft the vendor first, then the {kind}."));
            }
            else
            {
                description = $"{words} {vendor.Name} ({vendor.RefId}).";
            }
        }
        else if (standsFor is not null)
        {
            problems.Add(new Problem("supplier.for_unexpected", "--for is for a reseller or msp; a vendor stands for itself."));
        }

        var kept = new List<(string Path, string Sha256)>();
        foreach (var document in documents)
        {
            var file = Path.GetFullPath(Path.Combine(repo, document));
            if (!file.StartsWith(Path.GetFullPath(repo) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(file))
            {
                problems.Add(new Problem("supplier.document_missing", $"{document} does not exist in the repo. Keep the document under documents/ first, then name it relative to the repo."));
                continue;
            }

            kept.Add((document.Replace('\\', '/'), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file, cancellationToken)))));
        }

        return problems.Count > 0 ? Result.Fail<SupplierNotes>(problems) : Result.Ok(new SupplierNotes(description, kept));
    }

    private static async Task<Result<string>> DraftAsync(
        string repo, SupplierChoice choice, IReadOnlyList<LevelProposal> levels, SupplierNotes notes, string? system, DateOnly today, CancellationToken cancellationToken)
    {
        var relative = $"scope/third-parties/{SupplierFile.FileName(choice)}";
        var file = Path.Combine(repo, relative);
        if (File.Exists(file))
        {
            return Result.Fail<string>(new Problem("supplier.file_exists", $"{relative} already exists. Edit it in place, or choose another ref_id."));
        }

        var domains = await OnboardSystemCommand.DomainsAsync(repo, cancellationToken);
        var place = domains is [{ Perimeters: [var perimeter, ..] } domain, ..] ? new AuditPlace(domain.Name, perimeter.Name) : null;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, SupplierFile.Write(choice, levels, system, today, place, SupplierDrafter.Agent, notes), cancellationToken);
        return Result.Ok(relative);
    }
}
