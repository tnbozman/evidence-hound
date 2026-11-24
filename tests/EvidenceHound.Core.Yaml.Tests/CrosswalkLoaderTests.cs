using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using Json.Schema;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml.Tests;

public class CrosswalkLoaderTests
{
    private static readonly string ShippedPath = Path.Combine(AppContext.BaseDirectory, "files", "mappings", "crosswalks", "essential-eight-2023-to-nist-sp-800-171-rev2.yaml");

    private const string Small = """
        from: urn:example:risk:framework:source
        to: urn:example:risk:framework:target
        reviewed: {by: Security lead, on: 2026-11-20}
        requirements:
          - {ref: "T-1", title: "Target one", answered_by: [{ref: S-1, relationship: equal}, {ref: S-2, relationship: intersect}]}
          - {ref: "T-2", title: "Target two"}
        """;

    [Fact]
    public void Reads_the_targets_and_their_links()
    {
        var crosswalk = CrosswalkLoader.Parse(Small, "small.yaml").Value();

        crosswalk.FromUrn.ShouldBe("urn:example:risk:framework:source");
        crosswalk.ToUrn.ShouldBe("urn:example:risk:framework:target");
        crosswalk.Origin.ShouldBe("small.yaml");
        crosswalk.Targets.ShouldBe([new CrosswalkTarget("T-1", "Target one"), new CrosswalkTarget("T-2", "Target two")]);
        crosswalk.Links.ShouldBe(
        [
            new CrosswalkLink("T-1", "S-1", CrosswalkRelationship.Equal),
            new CrosswalkLink("T-1", "S-2", CrosswalkRelationship.Intersect),
        ]);
        crosswalk.Review.ShouldBe(new CrosswalkReview("Security lead", new DateOnly(2026, 11, 20)));
    }

    [Fact]
    public void A_crosswalk_nobody_has_reviewed_says_so()
    {
        var crosswalk = CrosswalkLoader.Parse(Small.Replace("reviewed: {by: Security lead, on: 2026-11-20}\n", "", StringComparison.Ordinal), "small.yaml").Value();

        crosswalk.Review.ShouldBeNull();
    }

    [Theory]
    [InlineData("relationship: equal", "relationship: same", "crosswalk.relationship_unknown")]
    [InlineData("from: urn:example:risk:framework:source\n", "", "crosswalk.key_missing")]
    [InlineData("to: urn:example:risk:framework:target", "to: target", "crosswalk.urn_invalid")]
    [InlineData("title: \"Target two\"", "title: \"Target two\", colour: blue", "crosswalk.key_unknown")]
    [InlineData("{ref: \"T-2\"", "{ref: \"T-1\"", "crosswalk.ref_repeated")]
    public void Problems_name_the_file_and_line(string from, string to, string code)
    {
        var problems = CrosswalkLoader.Parse(Small.Replace(from, to, StringComparison.Ordinal), "small.yaml").Problems();

        problems.ShouldContain(p => p.Code == code);
        problems.ShouldAllBe(p => p.Message.StartsWith("small.yaml", StringComparison.Ordinal));
    }

    [Fact]
    public void The_shipped_essential_eight_to_800_171_crosswalk_lists_every_rev_2_requirement()
    {
        var crosswalk = CrosswalkLoader.Parse(File.ReadAllText(ShippedPath), ShippedPath).Value();

        crosswalk.FromUrn.ShouldBe("urn:intuitem:risk:framework:essential-eight-2023");
        crosswalk.ToUrn.ShouldBe("urn:intuitem:risk:framework:nist-sp-800-171-rev2");
        crosswalk.Targets.Count.ShouldBe(110);
        crosswalk.Targets.Select(t => t.RefId).ShouldBeUnique();
        crosswalk.Links.ShouldNotBeEmpty();
        crosswalk.Review.ShouldBeNull();
    }

    [Fact]
    public void The_shipped_crosswalk_links_only_to_essential_eight_requirements_the_mapping_answers()
    {
        var crosswalk = CrosswalkLoader.Parse(File.ReadAllText(ShippedPath), ShippedPath).Value();
        var mapping = MappingLoader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "mappings", "essential-eight-2023.yaml")), "e8").Value();
        var mapped = mapping.Mapping.Requirements.Select(r => r.Key.Value).ToHashSet(StringComparer.Ordinal);

        crosswalk.Links.Select(l => l.SourceRef).Distinct().Where(r => !mapped.Contains(r)).ShouldBeEmpty();
    }

    [Fact]
    public void The_shipped_crosswalk_validates_against_the_schema()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "schemas", "crosswalk.schema.json")));

        var result = schema.Evaluate(ToJson(File.ReadAllText(ShippedPath)), new EvaluationOptions { OutputFormat = OutputFormat.List });

        result.IsValid.ShouldBeTrue(string.Join("; ", (result.Details ?? []).Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Values)));
        schema.Evaluate(ToJson(Small.Replace("relationship: equal", "relationship: same", StringComparison.Ordinal))).IsValid.ShouldBeFalse();
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
