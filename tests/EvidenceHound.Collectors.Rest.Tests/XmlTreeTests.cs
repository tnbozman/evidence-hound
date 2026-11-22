using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Engine;

namespace EvidenceHound.Collectors.Rest.Tests;

public sealed class XmlTreeTests
{
    private static readonly string[] ItemArrays = ["$.items.item"];

    [Fact]
    public void Repeated_and_single_elements_read_the_same()
    {
        var one = XmlTree.Parse("<items><item id=\"1\"><name>a</name></item></items>", ItemArrays).Value();
        var two = XmlTree.Parse("<items><item id=\"1\"><name>a</name></item><item id=\"2\"><name>b</name></item></items>", ItemArrays).Value();

        one["items"]!["item"].ShouldBeOfType<JsonArray>().Count.ShouldBe(1);
        two["items"]!["item"].ShouldBeOfType<JsonArray>().Count.ShouldBe(2);
        one["items"]!["item"]![0]!["@id"]!.GetValue<string>().ShouldBe("1");
        one["items"]!["item"]![0]!["name"]!.GetValue<string>().ShouldBe("a");
    }

    [Fact]
    public void Repeated_elements_become_a_list_even_when_not_listed()
    {
        var tree = XmlTree.Parse("<a><b>1</b><b>2</b></a>", []).Value();

        tree["a"]!["b"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["1", "2"]);
    }

    [Fact]
    public void Text_beside_attributes_is_under_text()
    {
        var tree = XmlTree.Parse("<version build=\"237\">21.0.1</version>", []).Value();

        tree["version"]!["@build"]!.GetValue<string>().ShouldBe("237");
        tree["version"]!["#text"]!.GetValue<string>().ShouldBe("21.0.1");
    }

    [Fact]
    public void Document_type_definitions_are_refused() =>
        XmlTree.Parse("<!DOCTYPE a [<!ENTITY x \"y\">]><a>&x;</a>", []).Problems().ShouldHaveSingleItem().Code.ShouldBe("rest.response_invalid");
}

public sealed class CsvTableTests
{
    [Fact]
    public void Reads_header_rows_as_objects()
    {
        var rows = CsvTable.Parse("id,name,note\n1,Alex,\"likes, commas\"\n2,Sam,\"says \"\"hi\"\"\"\n").Value();

        rows.Count.ShouldBe(2);
        rows[0]!["note"]!.GetValue<string>().ShouldBe("likes, commas");
        rows[1]!["note"]!.GetValue<string>().ShouldBe("says \"hi\"");
    }

    [Fact]
    public void Row_with_too_many_cells_is_reported_with_its_line() =>
        CsvTable.Parse("id,name\n1,Alex\n2,Sam,extra\n").Problems().ShouldHaveSingleItem().Message.ShouldContain("line 3");
}
