using EvidenceHound.Collectors.Sophos;

namespace Microsoft.Extensions.DependencyInjection;

public static class SophosServiceCollectionExtensions
{
    /// <summary>Registers the <c>sophos</c> collector.</summary>
    public static IServiceCollection AddSophosCollector(this IServiceCollection services) => services.AddCollector<SophosCollector>();
}
