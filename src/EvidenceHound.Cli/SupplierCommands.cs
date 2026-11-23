using System.CommandLine;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
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
        var standsFor = new Option<string?>("--for") { Description = "For a reseller or msp: the ref_id of the vendor whose product it sells or runs, already in scope/third-parties/." };
        var documents = new Option<string[]>("--document")
        {
            Description = "A document that was read for this draft and kept in the repo, as a path relative to the repo. Named in the file with its SHA-256. Repeat for more.",
            AllowMultipleArgumentsPerToken = false,
        };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, for an agent." };
        var draft = new Command("draft", "Draft a third party's file under scope/third-parties/ with a draft contract and an open review. A person approves it with 'hound approve'.")
        {
            repo, system, entityRef, entityName, solutionRef, solutionName, contractRef, contractName, levels, kind, standsFor, documents, json,
        };
        draft.SetAction(async (parseResult, cancellationToken) =>
        {
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var choice = new SupplierChoice(
                parseResult.GetRequiredValue(entityRef), parseResult.GetRequiredValue(entityName), parseResult.GetRequiredValue(solutionRef),
                parseResult.GetRequiredValue(solutionName), parseResult.GetRequiredValue(contractRef), parseResult.GetRequiredValue(contractName));
            var role = parseResult.GetValue(kind) ?? "vendor";
            var written = await Levels(parseResult.GetValue(levels) ?? [])
                .BindAsync(async read => (await NotesAsync(root, role, parseResult.GetValue(standsFor), parseResult.GetValue(documents) ?? [], cancellationToken)).Bind(notes => Result.Ok((read, notes))))
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
        return new Command("supplier", "Work with third parties in a compliance repo.") { draft };
    }

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
            if (standsFor is null)
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
