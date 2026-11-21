using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Runtime.Tests;

public class ConfigSchemaTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromText(TestFiles.ConfigSchema);

    [Fact]
    public void Example_config_validates_against_schema()
    {
        var result = Schema.Evaluate(ToJson(File.ReadAllText(TestFiles.ExampleConfigPath)), new EvaluationOptions { OutputFormat = OutputFormat.List });

        result.IsValid.ShouldBeTrue(string.Join("; ", (result.Details ?? []).Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Values)));
    }

    [Theory]
    [InlineData("token: env:HOUND_CA_TOKEN", "token: plain-value")]
    [InlineData("type: fake", "type: fake\n    colour: blue")]
    [InlineData("retention_days: 90", "retention_days: 0")]
    [InlineData("schedule: \"0 2 * * *\"", "schedule: daily")]
    [InlineData("compliance_assessment: EXAMPLE-BASELINE-2026", "")]
    public void Invalid_configs_fail_the_schema(string valid, string invalid)
    {
        var yaml = File.ReadAllText(TestFiles.ExampleConfigPath);
        yaml.ShouldContain(valid);

        Schema.Evaluate(ToJson(yaml.Replace(valid, invalid, StringComparison.Ordinal))).IsValid.ShouldBeFalse();
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
