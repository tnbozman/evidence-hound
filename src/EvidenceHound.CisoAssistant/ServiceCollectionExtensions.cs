using System.Net.Http.Headers;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Publishing;
using Microsoft.Extensions.Http.Resilience;

namespace Microsoft.Extensions.DependencyInjection;

public static class CisoAssistantServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICisoAssistantClient"/> as a typed HTTP client: token header, standard resilience
    /// (retry with jitter on 5xx, 408 and 429, timeouts, circuit breaker; POST is never retried) and
    /// every logged header value redacted.
    /// </summary>
    public static IServiceCollection AddCisoAssistantClient(this IServiceCollection services, CisoAssistantOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddHttpClient<ICisoAssistantClient, CisoAssistantClient>(http =>
            {
                http.BaseAddress = options.ApiUrl;
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", options.Token);
            })
            .RedactLoggedHeaders(_ => true)
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.Retry.Delay = options.RetryDelay;
                resilience.Retry.DisableFor(HttpMethod.Post);
            });
        return services;
    }
}
