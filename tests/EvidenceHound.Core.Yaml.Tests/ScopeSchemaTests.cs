using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml.Tests;

public class ScopeSchemaTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "schemas", "scope.schema.json")));

    public static TheoryData<string> ExampleFiles() =>
        [.. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "files", "compliance-repo", "scope"), "*.yaml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(AppContext.BaseDirectory, f))];

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public void Example_scope_files_validate_against_the_schema(string file)
    {
        Errors(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file))).ShouldBeEmpty();
    }

    [Fact]
    public void Test_scope_files_validate_against_the_schema()
    {
        Errors(ScopeFixture.Setup).ShouldBeEmpty();
        Errors(ScopeFixture.Controls).ShouldBeEmpty();
        Errors(ScopeFixture.Audit).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("ref_id: A\nname: A\nframework: urn:x\ndomain: D\nperimeter: P\n")]
    [InlineData("ref_id: A\nname: A\nframework: urn:x\ndomain: D\nperimeter: P\nunlisted: {result: not_applicable}\n")]
    [InlineData("ref_id: A\nname: A\nframework: urn:x\ndomain: D\nperimeter: P\nunlisted: {result: not_assessed}\nassessments:\n  - {ref: R, urn: 'urn:x:r', status: done, result: compliant}\n")]
    [InlineData("domains:\n  - name: D\n    perimeter: {name: P}\n    perimeters: [{name: Q}]\n")]
    [InlineData("domains:\n  - name: D\n    systems: [{name: S, type: secondary}]\n")]
    [InlineData("controls:\n  - {ref_id: AC-1, name: N, colour: blue}\n")]
    public void Invalid_scope_files_fail_the_schema(string yaml)
    {
        Errors(yaml).ShouldNotBeEmpty();
    }

    private static string[] Errors(string yaml)
    {
        var result = Schema.Evaluate(ToJson(yaml), new EvaluationOptions { OutputFormat = OutputFormat.List });
        return result.IsValid ? [] : [.. (result.Details ?? []).Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")), "invalid"];
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
        YamlScalarNode scalar => JsonValue.Create(scalar.Value),
        _ => throw new NotSupportedException(node.GetType().Name),
    };
}
