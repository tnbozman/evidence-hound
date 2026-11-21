using EvidenceHound.Collectors.Manual;
using EvidenceHound.Runtime.Collectors;

namespace Microsoft.Extensions.DependencyInjection;

public static class BuiltInCollectorServiceCollectionExtensions
{
    /// <summary>Registers the collectors this build ships.</summary>
    public static IServiceCollection AddBuiltInCollectors(this IServiceCollection services) =>
        services.AddCollector<FakeCollector>().AddCollector<ManualCollector>();
}
