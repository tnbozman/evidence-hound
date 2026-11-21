using EvidenceHound.Collectors;

namespace Microsoft.Extensions.DependencyInjection;

public static class CollectorServiceCollectionExtensions
{
    /// <summary>Registers a collector. The runner finds every registered <see cref="ICollector"/> and refuses to start if two share a type.</summary>
    public static IServiceCollection AddCollector<TCollector>(this IServiceCollection services)
        where TCollector : class, ICollector
    {
        throw new NotImplementedException();
    }
}
