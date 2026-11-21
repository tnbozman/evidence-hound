using Xunit.Sdk;

namespace EvidenceHound.Collectors.Testing.Tests;

public sealed class ContractKitTests : IDisposable
{
    private static readonly string RecordedWidgets = Path.Combine(AppContext.BaseDirectory, "recorded", "widgets.json");

    private readonly RecordedApi _working = RecordedApi.Start().RespondWithFile("GET", "/api/widgets", RecordedWidgets);

    private readonly RecordedApi _refusing = RecordedApi.Start().Respond("GET", "/api/widgets", 401, "{\"detail\":\"Invalid token.\"}");

    private CollectorScenario WorkingApi => new() { HttpClients = _working.HttpClients, Secrets = new Dictionary<string, string> { [TestCollectors.TokenKey] = TestCollectors.Token } };

    private CollectorScenario RefusingApi => new() { HttpClients = _refusing.HttpClients, Secrets = new Dictionary<string, string> { [TestCollectors.TokenKey] = TestCollectors.Token } };

    [Fact]
    public async Task Well_behaved_collector_passes_every_contract()
    {
        var contract = new Contract(TestCollectors.WellBehaved());

        contract.DescribesAtLeastOneCheck();
        await contract.EmitsOnlyDeclaredCheckIds();
        await contract.EvidenceFilesCanBeHashed();
        await contract.HonoursCancellation();
        await contract.ReturnsFailNotThrowsWhenCredentialsAreRefused();
        await contract.NeverLogsSecrets();
    }

    [Fact]
    public async Task Authenticating_collector_with_recorded_responses_passes_every_contract()
    {
        var contract = new Contract(TestCollectors.Authenticating(), WorkingApi, RefusingApi);

        contract.DescribesAtLeastOneCheck();
        await contract.EmitsOnlyDeclaredCheckIds();
        await contract.EvidenceFilesCanBeHashed();
        await contract.HonoursCancellation();
        await contract.ReturnsFailNotThrowsWhenCredentialsAreRefused();
        await contract.NeverLogsSecrets();
    }

    [Fact]
    public async Task Undeclared_check_id_fails_the_declared_check_ids_contract()
    {
        var contract = new Contract(TestCollectors.EmitsUndeclaredCheck());

        (await FailsAsync(contract.EmitsOnlyDeclaredCheckIds)).Message.ShouldContain("widget.unlisted");
        contract.DescribesAtLeastOneCheck();
        await contract.EvidenceFilesCanBeHashed();
    }

    [Fact]
    public void Collector_describing_no_checks_fails_the_describes_contract()
    {
        var failure = Should.Throw<Exception>(new Contract(TestCollectors.DescribesNoChecks()).DescribesAtLeastOneCheck);

        failure.ShouldBeAssignableTo<IAssertionException>();
        failure.Message.ShouldContain("no checks");
    }

    [Fact]
    public async Task Citing_a_file_it_did_not_emit_fails_the_hash_contract() =>
        (await FailsAsync(new Contract(TestCollectors.CitesMissingFile()).EvidenceFilesCanBeHashed)).Message.ShouldContain("missing.json");

    [Fact]
    public async Task Evidence_that_changes_between_reads_fails_the_hash_contract() =>
        (await FailsAsync(new Contract(TestCollectors.EvidenceChangesBetweenReads()).EvidenceFilesCanBeHashed)).Message.ShouldContain(TestCollectors.File);

    [Fact]
    public async Task Ignoring_cancellation_fails_the_cancellation_contract() =>
        (await FailsAsync(new Contract(TestCollectors.IgnoresCancellation()).HonoursCancellation)).Message.ShouldContain("cancel");

    [Fact]
    public async Task Throwing_on_refused_credentials_fails_the_credentials_contract() =>
        (await FailsAsync(new Contract(TestCollectors.ThrowsWhenRefused(), WorkingApi, RefusingApi).ReturnsFailNotThrowsWhenCredentialsAreRefused))
            .Message.ShouldContain(nameof(HttpRequestException));

    [Fact]
    public async Task Succeeding_with_refused_credentials_fails_the_credentials_contract() =>
        (await FailsAsync(new Contract(TestCollectors.SucceedsWhenRefused(), WorkingApi, RefusingApi).ReturnsFailNotThrowsWhenCredentialsAreRefused))
            .Message.ShouldContain("succeeded");

    [Fact]
    public async Task Declaring_secrets_without_a_refused_credentials_scenario_fails_the_credentials_contract() =>
        (await FailsAsync(new Contract(TestCollectors.Authenticating(), WorkingApi).ReturnsFailNotThrowsWhenCredentialsAreRefused))
            .Message.ShouldContain("RefusedCredentials");

    [Fact]
    public async Task Logging_a_secret_fails_the_secret_logging_contract()
    {
        var failure = await FailsAsync(new Contract(TestCollectors.LogsItsToken(), WorkingApi, RefusingApi).NeverLogsSecrets);

        failure.Message.ShouldContain(TestCollectors.TokenKey);
        failure.Message.ShouldNotContain(TestCollectors.Token);
    }

    [Fact]
    public async Task Declared_secret_missing_from_the_scenario_gets_a_made_up_value_that_is_still_checked()
    {
        var failure = await FailsAsync(new Contract(TestCollectors.LogsItsToken(), new(), new()).NeverLogsSecrets);

        failure.Message.ShouldContain(TestCollectors.TokenKey);
    }

    [Fact]
    public async Task Working_scenario_that_fails_reports_the_collectors_problems() =>
        (await FailsAsync(new Contract(TestCollectors.AlwaysFails()).EmitsOnlyDeclaredCheckIds)).Message.ShouldContain("widget.unreachable");

    public void Dispose()
    {
        _working.Dispose();
        _refusing.Dispose();
    }

    private static async Task<Exception> FailsAsync(Func<Task> contract)
    {
        var failure = (await Record.ExceptionAsync(contract)).ShouldNotBeNull();
        failure.ShouldBeAssignableTo<IAssertionException>($"Expected a contract failure, got {failure.GetType().Name}: {failure.Message}");
        return failure;
    }
}
