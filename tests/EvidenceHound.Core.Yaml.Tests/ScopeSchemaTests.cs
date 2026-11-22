using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;
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
        Errors(AccessScopeTests.Access).ShouldBeEmpty();
        Errors("custom_roles: [Evidence reviewer]\n" + AccessScopeTests.Access).ShouldBeEmpty();
        Errors(SettingsScopeTests.Settings).ShouldBeEmpty();
        Errors(SupplierScopeTests.Supplier).ShouldBeEmpty();
        Errors(SupplierScopeTests.OrganisationSetup).ShouldBeEmpty();
    }

    [Fact]
    public void Settings_schema_names_the_flags_and_general_settings_of_the_snapshot()
    {
        var defs = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "schemas", "scope.schema.json")))!["$defs"]!;
        Names(defs["settings"]!["properties"]!["feature_flags"]).ShouldBe(CisoAssistantSettings.Current.FeatureFlags.Keys.Order(StringComparer.Ordinal), ignoreOrder: true);
        Names(defs["settings"]!["properties"]!["general"]).ShouldBe(CisoAssistantSettings.Current.General.Order(StringComparer.Ordinal), ignoreOrder: true);
    }

    [Theory]
    [InlineData("ref_id: A\nname: A\nframework: urn:x\ndomain: D\nperimeter: P\n")]
    [InlineData("ref_id: A\nname: A\nframework: urn:x\ndomain: D\nperimeter: P\nunlisted: {result: not_applicable}\n")]
    [InlineData("ref_id: A\nname: A\nframework: urn:x\ndomain: D\nperimeter: P\nunlisted: {result: not_assessed}\nassessments:\n  - {ref: R, urn: 'urn:x:r', status: done, result: compliant}\n")]
    [InlineData("domains:\n  - name: D\n    perimeter: {name: P}\n    perimeters: [{name: Q}]\n")]
    [InlineData("domains:\n  - name: D\n    systems: [{name: S, type: secondary}]\n")]
    [InlineData("controls:\n  - {ref_id: AC-1, name: N, colour: blue}\n")]
    [InlineData("users:\n  - {email: it@example.com, password: not-a-real-secret}\n")]
    [InlineData("users:\n  - {email: it@example.com, service_account: robot}\n")]
    [InlineData("roles:\n  - {role: approver, users: [it@example.com]}\n")]
    [InlineData("teams:\n  - {name: IT team, colour: blue}\n")]
    [InlineData("feature_flags:\n  third_parties: true\n")]
    [InlineData("feature_flags:\n  tprm: yes\n")]
    [InlineData("general:\n  openai_api_key: not-a-real-secret\n")]
    [InlineData("general:\n  colour: blue\n")]
    [InlineData("feature_flags: {tprm: true}\nusers: []\n")]
    [InlineData("ref_id: SUP-1\nname: N\ncountry: Australia\n")]
    [InlineData("organisation:\n  entity:\n    name: N\n    trust: 3\n")]
    [InlineData("organisation:\n  entity:\n    description: No name.\n")]
    [InlineData("ref_id: SUP-1\nname: N\ncontracts:\n  - {ref_id: C-1, name: N, status: signed}\n")]
    [InlineData("ref_id: SUP-1\nname: N\nrepresentatives:\n  - {email: sam@example.com, password: not-a-real-secret}\n")]
    public void Invalid_scope_files_fail_the_schema(string yaml)
    {
        Errors(yaml).ShouldNotBeEmpty();
    }

    private static string[] Names(JsonNode? map) => [.. map!["propertyNames"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>())];

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
