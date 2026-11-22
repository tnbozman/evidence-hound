using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>
/// The built-in <c>sophos</c> collector. It signs in to Sophos Central with a read-only API credential, reads the network devices
/// the tenant manages (firewalls), saves each list as minimised JSON evidence, and evaluates their checks. Endpoint protection is
/// not read: endpoint evidence comes from the endpoint's own antivirus.
/// </summary>
public sealed class SophosCollector : ICollector
{
    public const string TypeName = "sophos";

    public string Type => TypeName;

    public string Version => "2.0.0";

    public CollectorDescription Describe() => new(
        [.. SophosChecks.All.Select(c => c.Id)],
        [
            new(SophosSettings.ClientId, "Client ID of the Sophos Central API credential.", Required: true),
            new(SophosSettings.TenantId, "ID of the tenant to read. Required for a partner or organization credential; a tenant credential reads its own tenant.", Required: false),
            new(SophosSettings.IdUrl, "Sophos ID address that issues tokens. Default: https://id.sophos.com/.", Required: false),
            new(SophosSettings.ApiUrl, "Sophos Central global API address. Default: https://api.central.sophos.com/.", Required: false),
            new(SophosSettings.IncludeIdentities, "true keeps host names, addresses and serial numbers in evidence. Default: false (hashed).", Required: false),
            new(SophosSettings.SupportedFirewallFirmware, "Comma-separated firewall firmware versions still in support, for example 20.0, 21.0.", Required: true),
        ],
        [
            new(SophosSettings.ClientSecret, "Client secret of the Sophos Central API credential.", Required: true),
        ]);

    public async Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return await SophosOptions.FromSettings(context.Settings)
            .Bind(options => Connection(options, context.Secrets).Map(connection => (options, connection)))
            .BindAsync(async pair =>
            {
                using var http = context.HttpClients.CreateClient(TypeName);
                var lists = await (await SophosAuth.SignInAsync(http, pair.connection, cancellationToken))
                    .BindAsync(session => ReadAsync(new SophosClient(http, session), cancellationToken));
                return lists.Bind(read => Evaluate(read, pair.options, context));
            });
    }

    private static Result<SophosConnection> Connection(SophosOptions options, IReadOnlyDictionary<string, Secret> secrets) =>
        secrets.GetValueOrDefault(SophosSettings.ClientSecret) is { } secret
            ? Result.Ok(new SophosConnection(options.ClientId, secret, options.TenantId, options.IdUrl, options.ApiUrl))
            : Result.Fail<SophosConnection>(new Problem("sophos.secret_missing", $"Secret '{SophosSettings.ClientSecret}' is required."));

    private static async Task<Result<IReadOnlyList<SophosCollection>>> ReadAsync(SophosClient client, CancellationToken cancellationToken)
    {
        var read = new List<Result<SophosCollection>>();
        foreach (var source in SophosSources.All)
        {
            read.Add(await client.GetAllAsync(source, cancellationToken));
        }

        return Result.Combine(read);
    }

    private static Result<CollectionResult> Evaluate(IReadOnlyList<SophosCollection> lists, SophosOptions options, CollectorContext context)
    {
        var byName = lists.ToDictionary(l => l.Source.Name, StringComparer.Ordinal);
        var data = SophosData.Parse(byName[SophosSources.Firewalls.Name].Items);
        var files = lists.Select(l => SophosEvidence.File(l, options.IncludeIdentities)).ToArray();
        var now = context.Clock.UtcNow;
        var checks = SophosChecks.All
            .Select(check => check.Evaluate(data, options, now))
            .Select(finding => CheckId.Create(finding.CheckId)
                .Bind(id => CheckResult.Create(id, finding.Outcome, finding.Observation, finding.Sources.Select(s => s + ".json"), now, context.Name)));
        return Result.Combine(checks).Map(results => new CollectionResult(results, files, []));
    }
}
