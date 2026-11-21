namespace EvidenceHound.CisoAssistant;

/// <summary>A CISO Assistant API operation: the HTTP method and the path as the OpenAPI schema writes it.</summary>
public sealed record ApiEndpoint(string Method, string Path);

/// <summary>Every CISO Assistant endpoint the client calls. Checked against the OpenAPI snapshot of the supported release.</summary>
public static class ApiEndpoints
{
    public static IReadOnlyList<ApiEndpoint> Used { get; } =
    [
        new("GET", "/api/health/"),
        new("GET", "/api/compliance-assessments/"),
        new("GET", "/api/compliance-assessments/{id}/object/"),
        new("GET", "/api/requirement-assessments/"),
        new("PATCH", "/api/requirement-assessments/{id}/"),
        new("GET", "/api/evidences/"),
        new("POST", "/api/evidences/"),
        new("GET", "/api/evidence-revisions/"),
        new("POST", "/api/evidence-revisions/"),
        new("GET", "/api/applied-controls/"),
        new("GET", "/api/applied-controls/{id}/"),
        new("PATCH", "/api/applied-controls/{id}/"),
        new("GET", "/api/applied-controls/{id}/object/"),
        new("GET", "/api/folders/"),
        new("GET", "/api/folders/{id}/object/"),
        new("GET", "/api/users/"),
        new("GET", "/api/frameworks/"),
        new("GET", "/api/perimeters/"),
        new("GET", "/api/perimeters/{id}/object/"),
        new("GET", "/api/assets/"),
        new("GET", "/api/assets/{id}/object/"),
        new("GET", "/api/evidences/{id}/object/"),
        new("POST", "/api/folders/"),
        new("PATCH", "/api/folders/{id}/"),
        new("DELETE", "/api/folders/{id}/"),
        new("POST", "/api/perimeters/"),
        new("PATCH", "/api/perimeters/{id}/"),
        new("DELETE", "/api/perimeters/{id}/"),
        new("POST", "/api/assets/"),
        new("PATCH", "/api/assets/{id}/"),
        new("DELETE", "/api/assets/{id}/"),
    ];
}
