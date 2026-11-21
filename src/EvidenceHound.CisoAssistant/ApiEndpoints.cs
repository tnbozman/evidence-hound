namespace EvidenceHound.CisoAssistant;

/// <summary>A CISO Assistant API operation: the HTTP method and the path as the OpenAPI schema writes it.</summary>
public sealed record ApiEndpoint(string Method, string Path);

/// <summary>Every CISO Assistant endpoint the client calls. Checked against the OpenAPI snapshot of the supported release.</summary>
public static class ApiEndpoints
{
    public static IReadOnlyList<ApiEndpoint> Used { get; } = [];
}
