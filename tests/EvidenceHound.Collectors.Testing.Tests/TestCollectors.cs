using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Collectors.Testing.Tests;

/// <summary>A collector whose description and collect step each test writes, so one class covers good and broken collectors.</summary>
internal sealed class ScriptedCollector(CollectorDescription description, Func<CollectorContext, CancellationToken, Task<Result<CollectionResult>>> collect) : ICollector
{
    public string Type => "scripted";

    public string Version => "1.0.0";

    public CollectorDescription Describe() => description;

    public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) => collect(context, cancellationToken);
}

/// <summary>Applies the contract kit to one collector. Not public, so xUnit does not run it; the kit's tests call its methods.</summary>
internal sealed class Contract(ICollector collector, CollectorScenario? working = null, CollectorScenario? refused = null) : CollectorContractTests<ICollector>
{
    protected override ICollector CreateCollector() => collector;

    protected override CollectorScenario Working => working ?? new();

    protected override CollectorScenario? RefusedCredentials => refused;
}

internal static class TestCollectors
{
    public const string Check = "widget.patched";

    public const string File = "widgets.json";

    public const string TokenKey = "api_token";

    public const string Token = "example-widget-token-5f0c2a9e7d1b4c3a";

    private static readonly Action<ILogger, int, Exception?> FetchedWidgets =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(1), "Fetched {Bytes} bytes of widgets");

    private static readonly Action<ILogger, string, Exception?> CallingWithToken =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(2), "Calling the widget API with token {Token}");

    public static CollectorDescription Description { get; } = new([Check], [], []);

    public static CollectorDescription AuthenticatingDescription { get; } = new([Check], [], [new(TokenKey, "Widget API token.", Required: true)]);

    /// <summary>Declares one check, honours cancellation, and reports that check with one stable evidence file.</summary>
    public static ScriptedCollector WellBehaved() => new(Description, (context, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Collected(context, [Check], [File], Evidence(File, "{\"widgets\":1}")));
    });

    /// <summary>Calls the widget API with its token; a refused token is returned as a problem.</summary>
    public static ScriptedCollector Authenticating() => new(AuthenticatingDescription, async (context, cancellationToken) =>
    {
        using var client = context.HttpClients.CreateClient("widget");
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/widgets");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.Secrets[TokenKey].Reveal());
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            context.Logger.LogWarning("Widget API refused the token");
            return Result.Fail<CollectionResult>(new Problem("widget.auth_refused", "The widget API refused the token."));
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        FetchedWidgets(context.Logger, body.Length, null);
        return Collected(context, [Check], [File], Evidence(File, body));
    });

    public static ScriptedCollector EmitsUndeclaredCheck() => new(Description, (context, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Collected(context, [Check, "widget.unlisted"], [File], Evidence(File, "{}")));
    });

    public static ScriptedCollector DescribesNoChecks() => new(new([], [], []), (context, _) => Task.FromResult(Collected(context, [], [], [])));

    public static ScriptedCollector CitesMissingFile() => new(Description, (context, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Collected(context, [Check], ["missing.json"], Evidence(File, "{}")));
    });

    public static ScriptedCollector EvidenceChangesBetweenReads()
    {
        var reads = 0;
        return new(Description, (context, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new CollectedEvidence(File, "application/json", () => new MemoryStream(Encoding.UTF8.GetBytes($"{{\"read\":{Interlocked.Increment(ref reads)}}}")));
            return Task.FromResult(Collected(context, [Check], [File], [file]));
        });
    }

    public static ScriptedCollector IgnoresCancellation() => new(Description, (context, _) =>
        Task.FromResult(Collected(context, [Check], [File], Evidence(File, "{}"))));

    public static ScriptedCollector ThrowsWhenRefused() => new(AuthenticatingDescription, async (context, cancellationToken) =>
    {
        using var client = context.HttpClients.CreateClient("widget");
        using var response = await client.GetAsync(new Uri("api/widgets", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        return Collected(context, [Check], [File], Evidence(File, "{}"));
    });

    public static ScriptedCollector SucceedsWhenRefused() => new(AuthenticatingDescription, (context, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Collected(context, [Check], [File], Evidence(File, "{}")));
    });

    public static ScriptedCollector LogsItsToken() => new(AuthenticatingDescription, (context, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallingWithToken(context.Logger, context.Secrets[TokenKey].Reveal(), null);
        return Task.FromResult(Collected(context, [Check], [File], Evidence(File, "{}")));
    });

    public static ScriptedCollector AlwaysFails() => new(Description, (_, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Fail<CollectionResult>(new Problem("widget.unreachable", "The widget API did not answer.")));
    });

    private static CollectedEvidence[] Evidence(string name, string json) => [CollectedEvidence.FromBytes(name, "application/json", Encoding.UTF8.GetBytes(json))];

    private static Result<CollectionResult> Collected(CollectorContext context, string[] checks, string[] cites, IReadOnlyList<CollectedEvidence> files) =>
        Result.Combine(checks.Select(id => CheckId.Create(id)
                .Bind(checkId => CheckResult.Create(checkId, CheckOutcome.Pass, "All widgets are patched.", cites, context.Clock.UtcNow, context.Name))))
            .Map(results => new CollectionResult(results, files, []));
}
