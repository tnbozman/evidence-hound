using EvidenceHound.Cli.Setup;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Cli.Tests;

/// <summary>s08-09: the wizards add to a YAML file in place, keeping its comments and layout.</summary>
public sealed class YamlEditTests
{
    private static readonly YamlMap Collector = new([
        new("name", new YamlText("b")),
        new("type", new YamlText("sophos")),
        new("settings", new YamlMap([new("client_id", new YamlText("x"))])),
    ]);

    [Fact]
    public void Sets_a_missing_key_on_a_list_item_and_leaves_an_equal_one_alone()
    {
        const string yaml = """
            domains:
              - name: Example Pty Ltd   # the company
                systems:
                  - name: Example firewall
                    type: primary
                  - name: Example 365
                    type: primary

            """;

        var edited = Ok(YamlEdit.SetScalar(yaml, "domains[name=Example Pty Ltd].systems[name=Example firewall].solution", "SOL-EX-FW"));

        edited.ShouldBe("""
            domains:
              - name: Example Pty Ltd   # the company
                systems:
                  - name: Example firewall
                    type: primary
                    solution: SOL-EX-FW
                  - name: Example 365
                    type: primary

            """);
        Ok(YamlEdit.SetScalar(edited, "domains[name=Example Pty Ltd].systems[name=Example firewall].solution", "SOL-EX-FW")).ShouldBe(edited);
        YamlEdit.SetScalar(edited, "domains[name=Example Pty Ltd].systems[name=Example firewall].solution", "SOL-OTHER")
            .ShouldBeOfType<Result<string>.Fail>().Errors.ShouldHaveSingleItem().Code.ShouldBe("setup.value_differs");
    }

    [Fact]
    public void Appends_to_a_block_list_in_its_indentation_keeping_comments()
    {
        const string yaml = """
            # top
            collectors:
                # first
                - name: a
                  type: manual   # trailing

            target:
              mapping: m.yaml

            """;

        var edited = Ok(YamlEdit.AppendItem(yaml, "collectors", Collector));

        edited.ShouldBe("""
            # top
            collectors:
                # first
                - name: a
                  type: manual   # trailing
                - name: b
                  type: sophos
                  settings:
                    client_id: x

            target:
              mapping: m.yaml

            """);
    }

    [Fact]
    public void Replaces_an_empty_flow_list()
    {
        var edited = Ok(YamlEdit.AppendItem("collectors: []   # none yet\ntarget: x\n", "collectors", Collector));

        edited.ShouldBe("collectors:   # none yet\n  - name: b\n    type: sophos\n    settings:\n      client_id: x\ntarget: x\n");
    }

    [Fact]
    public void Adds_a_missing_key_to_a_list_item_picked_by_name()
    {
        const string yaml = """
            domains:
              - name: Example Pty Ltd
                perimeter:
                  name: Corporate IT
              - name: Example Labs
                perimeter:
                  name: Lab
            """;
        var system = new YamlMap([new("name", new YamlText("Example firewall")), new("parents", new YamlFlowList(["Example 365", "1"]))]);

        var edited = Ok(YamlEdit.AppendItem(yaml + "\n", "domains[name=Example Pty Ltd].systems", system));

        edited.ShouldBe("""
            domains:
              - name: Example Pty Ltd
                perimeter:
                  name: Corporate IT
                systems:
                  - name: Example firewall
                    parents: [Example 365, "1"]
              - name: Example Labs
                perimeter:
                  name: Lab

            """);
    }

    [Fact]
    public void Quotes_values_yaml_would_read_as_something_else()
    {
        var item = new YamlMap([
            new("name", new YamlText("Example Pty Ltd")),
            new("owner", new YamlText("it@example.com")),
            new("schedule", new YamlText("0 3 * * *")),
            new("flag", new YamlText("yes")),
            new("days", new YamlText("14")),
            new("note", new YamlText("a: b # c \"d\"")),
        ]);

        var edited = Ok(YamlEdit.AppendItem("items: []\n", "items", item));

        edited.ShouldBe("items:\n  - name: Example Pty Ltd\n    owner: it@example.com\n    schedule: \"0 3 * * *\"\n    flag: \"yes\"\n    days: \"14\"\n    note: \"a: b # c \\\"d\\\"\"\n");
    }

    [Theory]
    [InlineData("systems: [A, B]\n", "systems: [A, B, Example firewall]\n")]
    [InlineData("systems: []\n", "systems: [Example firewall]\n")]
    [InlineData("systems:\n  - A\nname: x\n", "systems:\n  - A\n  - Example firewall\nname: x\n")]
    [InlineData("name: x\n", "name: x\nsystems: [Example firewall]\n")]
    public void Appends_a_name_to_a_list_of_names(string yaml, string expected) =>
        Ok(YamlEdit.AppendScalar(yaml, "systems", "Example firewall")).ShouldBe(expected);

    [Fact]
    public void A_path_that_names_nothing_is_a_problem()
    {
        var edited = YamlEdit.AppendItem("domains:\n  - name: Example Pty Ltd\n", "domains[name=Example Labs].systems", Collector);

        edited.ShouldBeOfType<Result<string>.Fail>().Errors[0].Code.ShouldBe("setup.path_missing");
    }

    private static string Ok(Result<string> result) => result.Match(text => text, problems => throw new ShouldAssertException(problems[0].Message));
}
