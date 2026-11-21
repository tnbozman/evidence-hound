using Azure.Core;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.M365;

/// <summary>
/// Reads Graph collections: a bearer token on every request, <c>@odata.nextLink</c> followed until the last page (only to the
/// Graph host), and <c>Retry-After</c> honoured on 429 and 503.
/// </summary>
internal sealed class GraphClient(HttpClient http, TokenCredential credential, Uri graphBase, IGraphWait wait)
{
    public const int MaxAttempts = 5;

    /// <summary>The problem code for a feature the tenant is not licensed for, such as Privileged Identity Management.</summary>
    public const string NotLicensed = "m365.graph_not_licensed";

    private readonly (HttpClient Http, TokenCredential Credential, Uri GraphBase, IGraphWait Wait) _parts = (http, credential, graphBase, wait);

    public Task<Result<GraphCollection>> GetAllAsync(GraphRequest request, CancellationToken cancellationToken) => throw new NotImplementedException(_parts.ToString());
}
