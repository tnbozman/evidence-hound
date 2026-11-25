using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Collectors.Manual;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Review;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.Configuration;

namespace EvidenceHound.Cli;

/// <summary>
/// What <c>hound status --json</c> and <c>hound next</c> both read (s18-05): the CISO Assistant connection and each manual source's due
/// date. Reads only, and never evidence content: a manual source is judged by its files' dates, not what they hold.
/// </summary>
internal static class HoundHealth
{
    /// <summary>How hound reached CISO Assistant: <c>Reachable</c> is null when it was not tried, and the note says why.</summary>
    internal sealed record Connection(bool? Reachable, string? Token, string Note);

    /// <summary>A manual source, when its newest file was attested or changed, and the work it needs now (null when none).</summary>
    internal sealed record ManualDue(string Source, string Owner, int EveryDays, DateTimeOffset? LastEvidence, NextItem? Item)
    {
        public DateOnly? Due => LastEvidence is { } last ? DateOnly.FromDateTime(last.UtcDateTime).AddDays(EveryDays) : null;

        public string State => Item?.Urgency switch
        {
            NextUrgency.Overdue => "overdue",
            NextUrgency.DueSoon => "due_soon",
            _ => "ok",
        };
    }

    /// <summary>The connection a config gives, checked with one read of the folder list; the options are null when none could be resolved.</summary>
    internal static async Task<(Connection Connection, CisoAssistantOptions? Options)> ConnectionAsync(
        IConfiguration configuration, FileInfo config, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var resolved = await PublishCommand.ConnectionAsync(configuration, config, registry, cancellationToken);
        return resolved is Result<CisoAssistantOptions>.Ok { Value: var options }
            ? (await CheckAsync(options, cancellationToken), options)
            : (new Connection(null, null, $"Not checked: {string.Join(" ", resolved is Result<CisoAssistantOptions>.Fail { Errors: var e } ? e.Select(p => p.Message) : [])}"), null);
    }

    /// <summary>Each configured manual source with its newest upload and what it needs now.</summary>
    internal static async Task<IReadOnlyList<ManualDue>> ManualSourcesAsync(DeploymentConfig deployment, DateOnly today, CancellationToken cancellationToken)
    {
        if (ManualProposalCommands.Sources(deployment, null) is not Result<IReadOnlyList<(string Name, ManualSource Manual)>>.Ok { Value: var found })
        {
            return [];
        }

        var due = new List<ManualDue>();
        foreach (var (name, manual) in found)
        {
            var last = await LastEvidenceAsync(manual.DropFolder, cancellationToken);
            due.Add(new ManualDue(name, manual.Owner, manual.DueDays, last, NextWork.Manual(name, manual.Owner, manual.DueDays, last, today)));
        }

        return due;
    }

    /// <summary>When the newest file in a drop folder was attested or, unattested, last changed; null when it holds none.</summary>
    private static async Task<DateTimeOffset?> LastEvidenceAsync(string dropFolder, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(dropFolder))
        {
            return null;
        }

        DateTimeOffset? last = null;
        foreach (var path in Directory.EnumerateFiles(dropFolder).Where(p => !p.EndsWith(Proposal.Suffix, StringComparison.Ordinal)))
        {
            DateTimeOffset? at = path.EndsWith(Attestation.Suffix, StringComparison.Ordinal)
                ? Attestation.Parse(await File.ReadAllTextAsync(path, cancellationToken), Path.GetFileName(path)) is Result<Attestation>.Ok { Value: var a } ? a.Date : null
                : new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            if (at is { } seen && (last is null || seen > last))
            {
                last = seen;
            }
        }

        return last;
    }

    /// <summary>One read of the folder list: reachable, and whether the token is accepted. Prints neither the token nor the response.</summary>
    private static async Task<Connection> CheckAsync(CisoAssistantOptions options, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.ApiUrl, "folders/?limit=1"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", options.Token);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new Connection(
                    true, "rejected", $"CISO Assistant rejected the token ({(int)response.StatusCode}): it may have expired or been revoked. Make a new personal access token for hound's service account."),
                _ when response.IsSuccessStatusCode => new Connection(
                    true, "accepted", "Reachable and the token is accepted. CISO Assistant's API does not give the token's expiry: check it under the service account's personal access tokens."),
                _ => new Connection(false, null, string.Create(CultureInfo.InvariantCulture, $"CISO Assistant answered {(int)response.StatusCode} at {options.ApiUrl}.")),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new Connection(false, null, $"CISO Assistant is not reachable at {options.ApiUrl}: {ex.Message}");
        }
    }
}
