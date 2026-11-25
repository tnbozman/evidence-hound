using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml.Tests;

public class MappingSchemaTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromText(RepoFiles.MappingSchema);

    [Fact]
    public void Example_mapping_validates_against_schema()
    {
        var result = Schema.Evaluate(ToJson(RepoFiles.ExampleMapping), new EvaluationOptions { OutputFormat = OutputFormat.List });

        result.IsValid.ShouldBeTrue(string.Join("; ", (result.Details ?? []).Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Values)));
    }

    [Theory]
    [InlineData("essential-eight-2023.yaml")]
    [InlineData("cmmc-2.0.yaml")]
    public void Reference_mapping_validates_against_schema(string file)
    {
        var yaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "mappings", file));

        var result = Schema.Evaluate(ToJson(yaml), new EvaluationOptions { OutputFormat = OutputFormat.List });

        result.IsValid.ShouldBeTrue(string.Join("; ", (result.Details ?? []).Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Values)));
    }

    [Theory]
    [InlineData("framework_urn: urn:example:x\nrequirements:\n  - {ref: '1', covers: some, rule: all_pass, checks: [a.b]}\n")]
    public void Covers_is_part_or_whole_in_the_schema(string yaml)
    {
        Schema.Evaluate(ToJson(yaml)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("framework_urn: urn:example:x\nrequirements: []\nmanual_only:\n  - {ref: '1'}\n")]
    [InlineData("framework_urn: urn:example:x\nrequirements: []\nmanual_only:\n  - {ref: '1', source: Record, owner: Alex}\n")]
    public void Invalid_manual_only_entries_fail_the_schema(string yaml)
    {
        Schema.Evaluate(ToJson(yaml)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("framework_urn: urn:example:x\nrequirements:\n  - {urn: 'urn:example:x:1', rule: all_pass, checks: [a.b]}\n")]
    [InlineData("framework_urn: urn:example:x\nrequirements:\n  - {ref: '1', rows: all, rule: all_pass, checks: [a.b]}\n")]
    public void Requirements_by_urn_or_every_row_of_a_ref_pass_the_schema(string yaml)
    {
        Schema.Evaluate(ToJson(yaml)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("framework_urn: urn:example:x\nrequirements:\n  - {ref: '1', urn: 'urn:example:x:1', rule: all_pass, checks: [a.b]}\n")]
    [InlineData("framework_urn: urn:example:x\nrequirements:\n  - {urn: 'urn:example:x:1', rows: all, rule: all_pass, checks: [a.b]}\n")]
    [InlineData("framework_urn: urn:example:x\nrequirements:\n  - {ref: '1', rows: some, rule: all_pass, checks: [a.b]}\n")]
    [InlineData("framework_urn: urn:example:x\nrequirements:\n  - {ref: '1', rule: most_pass, checks: [a.b]}\n")]
    [InlineData("framework_urn: urn:example:x\nrequirements:\n  - {ref: '1', rule: threshold, checks: [a.b]}\n")]
    [InlineData("framework_urn: urn:example:x\nrequirements:\n  - {ref: '1', rule: all_pass, checks: [A.B]}\n")]
    [InlineData("framework_urn: urn:example:x\nrequirements:\n  - {ref: '1', rule: all_pass, checks: [a.b], colour: blue}\n")]
    [InlineData("requirements: []\n")]
    public void Invalid_mappings_fail_the_schema(string yaml)
    {
        Schema.Evaluate(ToJson(yaml)).IsValid.ShouldBeFalse();
    }

    private static JsonElement ToJson(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return JsonSerializer.SerializeToElement(Convert(stream.Documents[0].RootNode));
    }

    private static JsonNode? Convert(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(c => KeyValuePair.Create(((YamlScalarNode)c.Key).Value!, Convert(c.Value)))),
        YamlSequenceNode list => new JsonArray([.. list.Children.Select(Convert)]),
        YamlScalarNode { Style: ScalarStyle.Plain, Value: var v } when long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => JsonValue.Create(n),
        YamlScalarNode scalar => JsonValue.Create(scalar.Value),
        _ => throw new NotSupportedException(node.GetType().Name),
    };
}
