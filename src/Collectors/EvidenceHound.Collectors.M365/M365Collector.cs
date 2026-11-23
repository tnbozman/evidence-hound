using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.M365;

/// <summary>
/// The built-in <c>m365</c> collector. It signs in as an Entra app registration, reads Microsoft Graph (read-only), saves each
/// collection it read as minimised JSON evidence, and evaluates identity, privileged access, device and Office checks.
/// </summary>
public sealed class M365Collector(IGraphCredentialFactory credentials, IGraphWait wait) : ICollector
{
    public const string TypeName = "m365";

    public string Type => TypeName;

    public string Version => "1.0.0";

    public CollectorDescription Describe() => new(
        [.. M365Checks.All.Select(c => c.Id)],
        [
            new(M365Settings.TenantId, "Directory (tenant) ID of the Entra tenant.", Required: true),
            new(M365Settings.ClientId, "Application (client) ID of the app registration the collector signs in as.", Required: true),
            new(M365Settings.GraphUrl, "Microsoft Graph address. Default: https://graph.microsoft.com/.", Required: false),
            new(M365Settings.SignIn, "app signs in as the app registration (the default); none sends no credential, for the Microsoft 365 dev server on localhost only.", Required: false),
            new(M365Settings.IncludeIdentities, "true keeps user principal names, mail addresses and people's names in evidence. Default: false (hashed).", Required: false),
            new(M365Settings.BreakGlassAccounts, "Comma-separated object IDs of break-glass accounts that Conditional Access policies may exclude.", Required: false),
            new(M365Settings.MfaPartialPercent, "Percentage of MFA-capable members at or above which MFA registration is partial rather than failed. Default: 90.", Required: false),
            new(M365Settings.PrivilegedRoles, "Comma-separated role template IDs treated as privileged. Default: 14 built-in administrator roles.", Required: false),
            new(M365Settings.GlobalAdminMin, "Fewest Global Administrators expected, break-glass accounts included. Default: 2.", Required: false),
            new(M365Settings.GlobalAdminMax, "Most Global Administrators expected, break-glass accounts included. Default: 4.", Required: false),
            new(M365Settings.CompliantPassPercent, "Percentage of compliant Intune-managed Windows devices needed to pass. Default: 95.", Required: false),
            new(M365Settings.CompliantPartialPercent, "Percentage of compliant Intune-managed Windows devices needed for partial. Default: 80.", Required: false),
            new(M365Settings.System, "The system in scope/setup.yaml this instance collects from, so scope/capabilities.yaml can say which capabilities it answers. Default: only capabilities routed to this instance by name count as its own.", Required: false),
            new(M365Settings.SupportedWindowsBuilds, "Comma-separated Windows builds still in support, for example 10.0.22631, 10.0.26100.", Required: true),
        ],
        [
            new(M365Settings.ClientCertificate, "PEM certificate and private key of the app registration (preferred).", Required: false),
            new(M365Settings.ClientSecret, "Client secret of the app registration, if no certificate is used.", Required: false),
        ]);

    public async Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return await M365Options.FromSettings(context.Settings)
            .Bind(options => GraphSignIn.For(options, credentials, context.Secrets).Map(credential => (options, credential)))
            .BindAsync(async pair =>
            {
                using var http = context.HttpClients.CreateClient(TypeName);
                var client = new GraphClient(http, pair.credential, pair.options.GraphUrl, wait);
                return (await ReadAsync(client, cancellationToken)).Bind(collections => Evaluate(collections, pair.options, context));
            });
    }

    /// <summary>Reads every collection. One the tenant is not licensed for is left out when the source allows it, so its checks can say so.</summary>
    private static async Task<Result<IReadOnlyList<(GraphSource Source, GraphCollection? Collection)>>> ReadAsync(GraphClient client, CancellationToken cancellationToken)
    {
        var read = new List<Result<(GraphSource, GraphCollection?)>>();
        foreach (var source in GraphSources.All)
        {
            var collection = await client.GetAllAsync(source.Request, cancellationToken);
            read.Add(collection.Match(
                found => Result.Ok<(GraphSource, GraphCollection?)>((source, found)),
                problems => source.NeedsLicence && problems.All(p => p.Code == GraphClient.NotLicensed)
                    ? Result.Ok<(GraphSource, GraphCollection?)>((source, null))
                    : Result.Fail<(GraphSource, GraphCollection?)>(problems)));
        }

        return Result.Combine(read);
    }

    private static Result<CollectionResult> Evaluate(IReadOnlyList<(GraphSource Source, GraphCollection? Collection)> collections, M365Options options, CollectorContext context)
    {
        var tenant = TenantData.Parse(collections.ToDictionary(c => c.Source.Name, c => c.Collection?.Items, StringComparer.Ordinal));
        var files = collections
            .Where(c => c.Collection is not null)
            .Select(c => GraphEvidence.File(c.Source.EvidenceFile, c.Collection!, options.IncludeIdentities))
            .ToArray();
        var now = context.Clock.UtcNow;
        var checks = M365Checks.All
            .Select(check => M365Checks.Evaluate(check, tenant, options, context.Capabilities.ProvidedElsewhere))
            .Select(finding => CheckId.Create(finding.CheckId)
                .Bind(id => CheckResult.Create(id, finding.Outcome, finding.Observation, finding.Sources.Select(s => s + ".json"), now, context.Name)));
        return Result.Combine(checks).Map(results => new CollectionResult(results, files, []));
    }
}
