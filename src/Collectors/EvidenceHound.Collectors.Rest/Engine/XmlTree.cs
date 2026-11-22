using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>Reads XML into the same tree as JSON.</summary>
public static class XmlTree
{
    public static Result<JsonObject> Parse(string xml, IReadOnlyList<string> arrays) => throw new NotImplementedException();
}
