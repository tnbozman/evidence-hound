using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>Reads CSV with a header row into a list of objects.</summary>
public static class CsvTable
{
    public static Result<JsonArray> Parse(string csv) => throw new NotImplementedException();
}
