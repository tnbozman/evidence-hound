using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>
/// Reads XML into the same tree as JSON, so checks use one path language: attributes become <c>@name</c>, text beside attributes or
/// elements becomes <c>#text</c>, and an element with only text becomes its text. Repeated elements become a list, and so does any
/// element whose path is listed in <c>arrays</c>, so one element and two read the same. Document type definitions are refused.
/// </summary>
public static class XmlTree
{
    public static Result<JsonObject> Parse(string xml, IReadOnlyList<string> arrays)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(arrays);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var root = XDocument.Load(reader).Root!;
            var lists = arrays.ToHashSet(StringComparer.Ordinal);
            return Result.Ok(new JsonObject { [root.Name.LocalName] = Convert(root, "$." + root.Name.LocalName, lists) });
        }
        catch (XmlException ex)
        {
            return Result.Fail<JsonObject>(new Problem("rest.response_invalid", $"The response is not XML this collector reads: {ex.Message}"));
        }
    }

    private static JsonNode? Convert(XElement element, string path, HashSet<string> lists)
    {
        var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToArray();
        var children = element.Elements().ToArray();
        var text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (attributes.Length == 0 && children.Length == 0)
        {
            return JsonValue.Create(text);
        }

        var node = new JsonObject();
        foreach (var attribute in attributes)
        {
            node["@" + attribute.Name.LocalName] = attribute.Value;
        }

        foreach (var group in children.GroupBy(c => c.Name.LocalName, StringComparer.Ordinal))
        {
            var childPath = $"{path}.{group.Key}";
            var values = group.Select(c => Convert(c, childPath, lists)).ToArray();
            node[group.Key] = values.Length > 1 || lists.Contains(childPath) ? new JsonArray(values) : values[0];
        }

        if (text.Length > 0)
        {
            node["#text"] = text;
        }

        return node;
    }
}
