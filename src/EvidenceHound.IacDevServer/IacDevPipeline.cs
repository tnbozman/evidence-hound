using EvidenceHound.Core.Checks;

namespace EvidenceHound.IacDevServer;

/// <summary>Which pipeline the dev server serves: every control in place, or every deviation applied.</summary>
public enum DevVariant
{
    Compliant,
    Gaps,
}

/// <summary>One run block of the control tests, and its failure message when it fails.</summary>
public sealed record ControlTest(string Name, string? Failure = null);

/// <summary>A resource the drift plan reports as changed outside Terraform.</summary>
public sealed record DriftedResource(string Address, string Type);

/// <summary>A named change to the pipeline, the checks it fails and how it changes the pipeline.</summary>
public sealed record Deviation(string Name, string Description, IReadOnlyDictionary<string, CheckOutcome> Fails, Func<IacDevPipeline, IacDevPipeline> Apply);

/// <summary>
/// The fictional Example Pty Ltd IaC pipeline for the GRC platform on Oracle Cloud: the repository <c>example/grc-infra</c>, its control
/// tests (one per run block of the onboarding tutorial's <c>controls.tftest.hcl</c>) and its nightly refresh-only plan. Described once;
/// <see cref="IacAnswers"/> turns it into GitHub Actions JSON, a JUnit report and a plan.
/// </summary>
public sealed record IacDevPipeline(string Repository, string HeadSha, IReadOnlyList<ControlTest> ControlTests, IReadOnlyList<DriftedResource> Drift, string? GivenReport = null)
{
    /// <summary>The token the collector sends; the dev server accepts any.</summary>
    public static readonly string Token = "iac-dev-token-not-a-secret";

    public static readonly string ControlsWorkflow = "controls.yml";

    public static readonly string DriftWorkflow = "drift.yml";

    /// <summary>Every resource of the tutorial's Terraform, each a no-op in the refresh-only plan.</summary>
    public static readonly IReadOnlyList<DriftedResource> Resources =
    [
        new("oci_core_vcn.grc", "oci_core_vcn"),
        new("oci_core_nat_gateway.grc", "oci_core_nat_gateway"),
        new("oci_core_service_gateway.grc", "oci_core_service_gateway"),
        new("oci_core_route_table.private", "oci_core_route_table"),
        new("oci_core_security_list.grc", "oci_core_security_list"),
        new("oci_core_subnet.private", "oci_core_subnet"),
        new("oci_core_drg.grc", "oci_core_drg"),
        new("oci_core_drg_attachment.grc", "oci_core_drg_attachment"),
        new("oci_core_cpe.xgs", "oci_core_cpe"),
        new("oci_core_ipsec.office", "oci_core_ipsec"),
        new("oci_bastion_bastion.grc", "oci_bastion_bastion"),
        new("oci_core_instance.grc", "oci_core_instance"),
        new("oci_core_volume_backup_policy.weekly", "oci_core_volume_backup_policy"),
        new("oci_core_volume_backup_policy_assignment.grc", "oci_core_volume_backup_policy_assignment"),
        new("oci_objectstorage_bucket.backups", "oci_objectstorage_bucket"),
        new("oci_identity_dynamic_group.grc_vm", "oci_identity_dynamic_group"),
        new("oci_identity_policy.backups", "oci_identity_policy"),
    ];

    public static IacDevPipeline Compliant { get; } = new(
        "example/grc-infra",
        "0123456789abcdef0123456789abcdef01234567",
        [
            new("vm_has_no_public_ip"),
            new("https_only_from_the_office"),
            new("office_range_is_not_the_internet"),
            new("no_ssh_from_the_internet"),
            new("vpn_routes_only_the_office"),
            new("storage_encrypted"),
            new("vm_metadata_v2_only"),
            new("backups_private_and_in_another_region"),
            new("backups_retention_locked"),
            new("backup_retention_cannot_be_shortened"),
            new("only_backup_admins_reach_backups"),
            new("volume_backed_up_weekly"),
        ],
        []);

    public static IReadOnlyList<Deviation> Deviations { get; } =
    [
        new(
            "public-ip",
            "A change gives the GRC platform's VM a public IP address, and the control test vm_has_no_public_ip fails.",
            new Dictionary<string, CheckOutcome>(StringComparer.Ordinal)
            {
                ["infrastructure.tests.pass"] = CheckOutcome.Fail,
                ["infrastructure.controls.vm_has_no_public_ip"] = CheckOutcome.Fail,
            },
            p => p with
            {
                ControlTests = [.. p.ControlTests.Select(t => t.Name == "vm_has_no_public_ip"
                    ? t with { Failure = "vm_has_no_public_ip: the GRC platform's VM must not have a public IP address." }
                    : t)],
            }),
        new(
            "drift",
            "Someone changed the backup bucket in the OCI console: the nightly plan finds it no longer matches the code.",
            new Dictionary<string, CheckOutcome>(StringComparer.Ordinal) { ["infrastructure.drift.none"] = CheckOutcome.Fail },
            p => p with { Drift = [.. p.Drift, new("oci_objectstorage_bucket.backups", "oci_objectstorage_bucket")] }),
    ];

    public static IacDevPipeline For(DevVariant variant) =>
        variant == DevVariant.Compliant ? Compliant : Deviations.Aggregate(Compliant, (pipeline, deviation) => pipeline.With(deviation));

    /// <summary>The settings for a rest collector with <paramref name="profile"/> reading this pipeline at <paramref name="url"/>.</summary>
    public static Dictionary<string, string> Settings(Uri url, string profile)
    {
        ArgumentNullException.ThrowIfNull(url);
        return new(StringComparer.Ordinal)
        {
            ["profile"] = profile,
            ["api_url"] = url.ToString(),
            ["repository"] = Compliant.Repository,
            ["workflow"] = profile == "terraform-drift" ? DriftWorkflow : ControlsWorkflow,
        };
    }

    public IacDevPipeline With(Deviation deviation)
    {
        ArgumentNullException.ThrowIfNull(deviation);
        return deviation.Apply(this);
    }

    /// <summary>Serves <paramref name="report"/>, a JUnit report from <c>terraform test -junit-xml</c>, in place of the described tests.</summary>
    public IacDevPipeline WithReport(string report) => this with { GivenReport = report };
}
