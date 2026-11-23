using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Collectors.M365.Tests;

/// <summary>s09-29: the Microsoft 365 checks are grouped by capability, and a capability another product provides is not judged here.</summary>
public sealed class M365CapabilityTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 2, 0, 0, TimeSpan.Zero);

    private readonly RecordedTenant _tenant = RecordedTenant.Start();

    [Fact]
    public async Task Unused_capability_is_not_applicable_naming_the_provider()
    {
        var elsewhere = new CollectorCapabilities(new Dictionary<string, string>
        {
            ["mfa"] = "Example identity provider (audit EX-IDP-E8-2026)",
            ["rmom"] = "Example app control (audit EX-APPCTL-E8-2026)",
        });

        var checks = (await CollectAsync(elsewhere)).Value().Checks.ToDictionary(c => c.CheckId.Value);

        foreach (var id in new[] { "identity.mfa.registered_all_users", "identity.mfa.enforced_conditional_access", "identity.legacy_auth_blocked", "identity.mfa.phishing_resistant_admins", "apps.office.macros_blocked_from_internet" })
        {
            checks[id].Outcome.ShouldBe(CheckOutcome.NotApplicable, id);
            checks[id].Observation.ShouldContain("scope/capabilities.yaml");
            checks[id].EvidenceRefs.ShouldBeEmpty();
        }

        checks["identity.mfa.enforced_conditional_access"].Observation.ShouldContain("Example identity provider (audit EX-IDP-E8-2026)");
        checks["apps.office.macros_blocked_from_internet"].Observation.ShouldContain("Example app control (audit EX-APPCTL-E8-2026)");

        // The ASR Office rules serve macros and user application hardening; hardening is still Microsoft 365's, so the check runs.
        checks["endpoint.asr.office_rules_enabled"].Outcome.ShouldBe(CheckOutcome.Pass);
        checks["identity.privileged.global_admin_count"].Outcome.ShouldBe(CheckOutcome.Pass);
        checks["endpoint.os.supported_versions"].Outcome.ShouldBe(CheckOutcome.Pass);
    }

    [Fact]
    public async Task Default_without_a_map_keeps_todays_checks()
    {
        var checks = (await CollectAsync(CollectorCapabilities.All)).Value().Checks;

        checks.Count.ShouldBe(14);
        checks.ShouldAllBe(c => c.Outcome == CheckOutcome.Pass);
    }

    [Fact]
    public void Every_check_names_its_capabilities()
    {
        string[] capabilities = ["ac", "pa", "rmom", "uah", "rap", "pos", "mfa", "rb", "edr"];

        M365Checks.All.ShouldAllBe(c => c.Capabilities.Count > 0 && c.Capabilities.All(capabilities.Contains));
        M365Checks.Capabilities.ShouldBe(["mfa", "rap", "pos", "rmom", "uah", "ac"]);
    }

    public void Dispose() => _tenant.Dispose();

    private Task<Result<CollectionResult>> CollectAsync(CollectorCapabilities capabilities) =>
        new M365Collector(new TestCredentialFactory(), new RecordingWait()).CollectAsync(
            new CollectorContext("example-m365", new CollectorSettings(_tenant.Settings()), new Dictionary<string, Secret>(), new Clock(), NullLogger.Instance, _tenant.HttpClients)
            {
                Capabilities = capabilities,
            },
            TestContext.Current.CancellationToken);

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
