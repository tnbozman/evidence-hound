using System.Net.Http.Headers;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Scope;
using Microsoft.Extensions.Http.Resilience;

namespace Microsoft.Extensions.DependencyInjection;

public static class CisoAssistantServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICisoAssistantClient"/> the scope reader (<see cref="IScopeStateSource"/>) and writer (<see cref="IScopeWriter"/>) as typed HTTP clients: token header, standard resilience
    /// (retry with jitter on 5xx, 408 and 429, timeouts, circuit breaker; POST is never retried) and
    /// every logged header value redacted.
    /// </summary>
    public static IServiceCollection AddCisoAssistantClient(this IServiceCollection services, CisoAssistantOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Configure(services.AddHttpClient<ICisoAssistantClient, CisoAssistantClient>(), options);
        Configure(services.AddHttpClient<IScopeStateSource, ScopeStateReader>(), options);
        Configure(services.AddHttpClient<IScopeExportSource, ScopeStateReader>(), options);
        Configure(services.AddHttpClient<IScopeWriter, ScopeWriter>(), options);
        Configure(services.AddHttpClient<EvidenceHound.Core.Reporting.IPostureSource, PostureReader>(), options);
        return services;
    }

    private static void Configure(IHttpClientBuilder client, CisoAssistantOptions options) =>
        client
            .ConfigureHttpClient(http =>
            {
                http.BaseAddress = options.ApiUrl;
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", options.Token);
                // CISO Assistant translates built-in names (such as roles) to the request's language; hound reads them in English.
                http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
            })
            .RedactLoggedHeaders(_ => true)
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.Retry.Delay = options.RetryDelay;
                resilience.Retry.DisableFor(HttpMethod.Post);
            });
}
