using EvidenceHound.Collectors.Manual;
using EvidenceHound.Collectors.Rest;
using EvidenceHound.Collectors.VulnScan;
using EvidenceHound.Runtime.Collectors;

namespace Microsoft.Extensions.DependencyInjection;

public static class BuiltInCollectorServiceCollectionExtensions
{
    /// <summary>Registers the collectors this build ships.</summary>
    public static IServiceCollection AddBuiltInCollectors(this IServiceCollection services) =>
        services.AddCollector<FakeCollector>().AddCollector<ManualCollector>().AddM365Collector().AddRestCollector().AddCollector<VulnScanCollector>();

    /// <summary>The rest collector, with a client that hands every redirect back to it so it can refuse one to another host.</summary>
    private static IServiceCollection AddRestCollector(this IServiceCollection services)
    {
        services.AddHttpClient(RestCollector.TypeName).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        return services.AddCollector<RestCollector>();
    }
}
