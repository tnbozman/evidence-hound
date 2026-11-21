using Hound.Template.Collector;

namespace Microsoft.Extensions.DependencyInjection;

public static class TemplateStemCollectorServiceCollectionExtensions
{
    /// <summary>Registers the TemplateStem collector, type <c>template_type</c>.</summary>
    public static IServiceCollection AddTemplateStemCollector(this IServiceCollection services) => services.AddCollector<TemplateStemCollector>();
}
