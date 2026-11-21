using EvidenceHound.Collectors.M365;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class M365ServiceCollectionExtensions
{
    /// <summary>Registers the <c>m365</c> collector with Azure.Identity credentials and real waits, unless others are already registered.</summary>
    public static IServiceCollection AddM365Collector(this IServiceCollection services)
    {
        services.TryAddSingleton<IGraphCredentialFactory, AzureIdentityCredentials>();
        services.TryAddSingleton<IGraphWait, SystemGraphWait>();
        return services.AddCollector<M365Collector>();
    }
}
