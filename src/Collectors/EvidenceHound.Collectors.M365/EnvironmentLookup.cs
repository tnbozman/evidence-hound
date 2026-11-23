using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.M365;

/// <summary>The subscribed licences (SKU part numbers) and the kinds of Intune configuration (<c>@odata.type</c>) a tenant holds, sorted.</summary>
public sealed record TenantSignals(IReadOnlyList<string> Licences, IReadOnlyList<string> ConfigurationTypes);

/// <summary>
/// The read-only lookups that pre-fill the capability map in <c>hound onboard environment</c>, signed in as a configured <c>m365</c>
/// collector. Two Graph reads; nothing is written to the tenant and nothing is kept as evidence. A tenant without Intune has no
/// configuration kinds.
/// </summary>
public sealed class EnvironmentLookup(IGraphCredentialFactory credentials, IGraphWait wait)
{
    public const string LicencePermission = "Organization.Read.All";

    private static readonly GraphRequest Skus = new("v1.0/subscribedSkus?$select=skuPartNumber,capabilityStatus", LicencePermission);

    private static readonly GraphRequest Configurations = new("v1.0/deviceManagement/deviceConfigurations?$select=id", "DeviceManagementConfiguration.Read.All");

    public async Task<Result<TenantSignals>> ReadAsync(
        CollectorSettings settings, IReadOnlyDictionary<string, Secret> secrets, IHttpClientFactory httpClients, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(httpClients);
        return await M365Options.FromSettings(settings)
            .Bind(options => GraphSignIn.For(options, credentials, secrets).Map(credential => (options, credential)))
            .BindAsync(async pair =>
            {
                using var http = httpClients.CreateClient(M365Collector.TypeName);
                var client = new GraphClient(http, pair.credential, pair.options.GraphUrl, wait);
                var skus = await client.GetAllAsync(Skus, cancellationToken);
                if (skus is Result<GraphCollection>.Fail { Errors: var problems })
                {
                    return Result.Fail<TenantSignals>(problems);
                }

                var configurations = await client.GetAllAsync(Configurations, cancellationToken);
                return configurations.Match(
                    found => Result.Ok(Signals(((Result<GraphCollection>.Ok)skus).Value.Items, found.Items)),
                    refused => refused.All(p => p.Code == GraphClient.NotLicensed)
                        ? Result.Ok(Signals(((Result<GraphCollection>.Ok)skus).Value.Items, []))
                        : Result.Fail<TenantSignals>(refused));
            });
    }

    private static TenantSignals Signals(JsonArray skus, JsonArray configurations) => new(
        Sorted(skus.Where(s => s?["capabilityStatus"]?.GetValue<string>() is null or "Enabled" or "Warning").Select(s => s?["skuPartNumber"]?.GetValue<string>())),
        Sorted(configurations.Select(c => c?["@odata.type"]?.GetValue<string>())));

    private static string[] Sorted(IEnumerable<string?> values) => [.. values.OfType<string>().Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
