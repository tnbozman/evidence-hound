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
            new(M365Settings.IncludeIdentities, "true keeps user principal names, mail addresses and people's names in evidence. Default: false (hashed).", Required: false),
            new(M365Settings.BreakGlassAccounts, "Comma-separated object IDs of break-glass accounts that Conditional Access policies may exclude.", Required: false),
            new(M365Settings.MfaPartialPercent, "Percentage of MFA-capable members at or above which MFA registration is partial rather than failed. Default: 90.", Required: false),
            new(M365Settings.PrivilegedRoles, "Comma-separated role template IDs treated as privileged. Default: 14 built-in administrator roles.", Required: false),
        ],
        [
            new(M365Settings.ClientCertificate, "PEM certificate and private key of the app registration (preferred).", Required: false),
            new(M365Settings.ClientSecret, "Client secret of the app registration, if no certificate is used.", Required: false),
        ]);

    public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) => throw new NotImplementedException($"{credentials}{wait}");
}
