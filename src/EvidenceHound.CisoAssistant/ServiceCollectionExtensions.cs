using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Publishing;

namespace Microsoft.Extensions.DependencyInjection;

public static class CisoAssistantServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ICisoAssistantClient"/> as a typed HTTP client. Not built yet.</summary>
    public static IServiceCollection AddCisoAssistantClient(this IServiceCollection services, CisoAssistantOptions options) =>
        throw new NotImplementedException();
}
