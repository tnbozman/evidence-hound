using EvidenceHound.Collectors;
using EvidenceHound.Core.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

public sealed class CollectorsCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-collectors-tests-").FullName;

    [Fact]
    public async Task List_shows_each_collector_with_its_check_ids_and_settings()
    {
        var exitCode = await Run(services => services.AddCollector<WidgetCollector>(), "collectors", "list");

        exitCode.ShouldBe(ExitCodes.Ok);
        _output.ToString().ReplaceLineEndings("\n").ShouldBe("""
            fake 1.0.0
              checks: backup.daily, backup.restore_tested, identity.mfa.admins, identity.mfa.users
              settings: tenant (optional)
              secrets: none
            m365 1.0.0
              checks: apps.office.macros_blocked_from_internet, endpoint.application_control.enforced, endpoint.application_control.file_types_covered, endpoint.asr.office_rules_enabled, endpoint.managed.compliant_ratio, endpoint.os.supported_versions, identity.legacy_auth_blocked, identity.mfa.enforced_conditional_access, identity.mfa.phishing_resistant_admins, identity.mfa.registered_all_users, identity.privileged.admins_cloud_only, identity.privileged.global_admin_count, identity.privileged.pim_eligible_not_permanent
              settings: tenant_id (required), client_id (required), graph_url (optional), sign_in (optional), include_identities (optional), break_glass_accounts (optional), mfa_partial_percent (optional), privileged_roles (optional), global_admin_min (optional), global_admin_max (optional), compliant_pass_percent (optional), compliant_partial_percent (optional), application_control_groups (optional), system (optional), supported_windows_builds (required)
              secrets: client_certificate (optional), client_secret (optional)
            manual 1.0.0
              checks: set per instance by setting 'checks'
              settings: drop_folder (required), patterns (optional), checks (required), due_days (required), owner (required)
              secrets: none
            rest 1.0.0
              checks: set per instance by its settings
              settings: profile (required), profiles_path (optional), include_identities (optional), allow_writes (optional), require_profile_tests (optional)
              secrets: none
            widget 0.1.0
              checks: widget.patched
              settings: site (required)
              secrets: api_key (required)

            """);
    }

    [Fact]
    public async Task Duplicate_collector_types_stop_the_cli_at_startup()
    {
        var exitCode = await Run(services => services.AddCollector<SecondFakeCollector>(), "collectors", "list");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _output.ToString().ShouldBeEmpty();
        _logs.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("'fake'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Config_validate_checks_the_settings_a_registered_collector_describes()
    {
        var config = Path.Combine(_workDir, "widget.yaml");
        await File.WriteAllTextAsync(config, """
            ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_CA_TOKEN"}
            evidence: {root: ./evidence}
            collectors:
              - {name: widgets, type: widget, schedule: "0 2 * * *", secrets: {api_key: "env:HOUND_WIDGET_KEY"}}
            target: {mapping: ./mapping.yaml, compliance_assessment: EXAMPLE-1}
            """, TestContext.Current.CancellationToken);

        var exitCode = await Run(services => services.AddCollector<WidgetCollector>(), "config", "validate", config);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("needs setting 'site'");
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private Task<int> Run(Action<IServiceCollection> services, params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs), services, TestContext.Current.CancellationToken);

    private sealed class WidgetCollector : ICollector
    {
        public string Type => "widget";

        public string Version => "0.1.0";

        public CollectorDescription Describe() => new(
            ["widget.patched"],
            [new("site", "The widget site to read.", Required: true)],
            [new("api_key", "Reference to the widget API key.", Required: true)]);

        public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new CollectionResult([], [], [])));
    }

    private sealed class SecondFakeCollector : ICollector
    {
        public string Type => "fake";

        public string Version => "0.1.0";

        public CollectorDescription Describe() => new([], [], []);

        public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new CollectionResult([], [], [])));
    }
}
