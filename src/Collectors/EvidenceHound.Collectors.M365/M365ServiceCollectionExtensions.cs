using EvidenceHound.Collectors.M365;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class M365ServiceCollectionExtensions
{
    /// <summary>Registers the <c>m365</c> collector with Azure.Identity credentials and real waits, and the read-only lookups that onboarding uses, unless others are already registered.</summary>
    public static IServiceCollection AddM365Collector(this IServiceCollection services)
    {
        services.TryAddSingleton<IGraphCredentialFactory, AzureIdentityCredentials>();
        services.TryAddSingleton<IGraphWait, SystemGraphWait>();
        services.TryAddSingleton<EnvironmentLookup>();
        return services.AddCollector<M365Collector>();
    }
}
