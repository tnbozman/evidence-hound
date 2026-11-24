using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s11-11: the National AI Centre's AI register template (the Excel version) and screening questions, traced to where the register keeps
/// each one. The trace tables are in <c>docs/ai-grc.md</c>; every <c>ai</c> key they name must exist in the scope schema.
/// </summary>
public partial class AiRegisterTemplateTests
{
    /// <summary>The template's columns, as its Excel version heads them (with "foreseeable" spelled out).</summary>
    private static readonly string[] Columns =
    [
        "Name and version", "Owner", "Status", "Source", "Purpose/business goals", "Intended use case(s)", "Known limitations and prohibited use",
        "Any foreseeable misuse", "Data sources and type", "Registered date", "Screening outcome", "Key stakeholders affected",
    ];

    private static string Files => Path.Combine(AppContext.BaseDirectory, "files");

    [Fact]
    public void Every_template_column_is_traced()
    {
        var rows = Table("## The National AI Centre's register template");

        rows.Select(r => r[0]).ShouldBe(Columns);
        rows.ShouldAllBe(r => r[1].Length > 0);
        AiKeys(rows).ShouldBeSubsetOf(SchemaKeys());
    }

    [Fact]
    public void Every_screening_question_is_traced()
    {
        var rows = Table("## The National AI Centre's screening questions");

        rows.Select(r => r[0]).ShouldBe(["1", "2", "3", "4", "5", "6", "7"]);
        rows.ShouldAllBe(r => r[1].Length > 0 && r[2].Length > 0);
    }

    /// <summary>The rows of the first table after <paramref name="heading"/>, header and rule left out, as trimmed cells.</summary>
    private static List<string[]> Table(string heading)
    {
        var doc = File.ReadAllText(Path.Combine(Files, "docs", "ai-grc.md"));
        var section = doc.Split(heading + "\n")[1].Split("\n## ")[0];
        return [.. section.Split('\n')
            .Where(l => l.StartsWith('|'))
            .Skip(2)
            .Select(l => l.Trim('|').Split(" | ").Select(c => c.Trim()).ToArray())];
    }

    private static IEnumerable<string> AiKeys(IEnumerable<string[]> rows) =>
        rows.SelectMany(r => AiKey().Matches(r[1]).Select(m => m.Groups[1].Value)).Distinct();

    private static IEnumerable<string> SchemaKeys()
    {
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(Files, "schemas", "scope.schema.json")))!;
        var ai = schema["$defs"]!["system"]!["properties"]!["ai"]!;
        return ai["properties"]!.AsObject().Select(p => p.Key);
    }

    /// <summary>A backticked <c>ai.</c> key, such as <c>`ai.purpose`</c>.</summary>
    [GeneratedRegex(@"`ai\.([a-z_]+)`")]
    private static partial Regex AiKey();
}
