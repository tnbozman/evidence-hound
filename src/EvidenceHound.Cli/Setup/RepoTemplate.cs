using System.Globalization;
using static EvidenceHound.Cli.Setup.YamlEdit;

namespace EvidenceHound.Cli.Setup;

/// <summary>
/// The answers <c>hound init</c> builds a new organisation's compliance repo from. The audit runs by <c>Interval</c>, <c>quarter</c> or
/// <c>month</c>, from <c>Starts</c>: the first day of the financial-year quarter, or the month, holding the day hound init ran.
/// </summary>
internal sealed record NewOrganisation(string Name, string Code, string CisoAssistantUrl, string TimeZone, int Level, string Interval, DateOnly Starts, string Owner, string EvidenceRoot)
{
    /// <summary>The audit series, <c>&lt;CODE&gt;-E8</c>; each period's ref ID reads <c>&lt;CODE&gt;-E8-Q2-2027FY</c> (s09-46).</summary>
    public string AuditRef => $"{Code}-E8";

    public string AuditFile => $"scope/audits/{AuditRef.ToLowerInvariant()}.yaml";
}

/// <summary>The files of a new compliance repo, by path relative to the repo, as text. Pure: the caller writes them.</summary>
internal static class RepoTemplate
{
    public const string Framework = "urn:intuitem:risk:framework:essential-eight-2023";
    public const string Library = "urn:intuitem:risk:library:essential-eight-2023";
    public const string HealthControl = "AC-AUTO-EVIDENCE";
    public const string Perimeter = "Corporate IT";

    /// <summary>The files hound writes from its own copies: the Essential Eight mapping and snapshot, and the template's .gitignore.</summary>
    public static readonly IReadOnlyList<string> Copied = ["mappings/essential-eight-2023.yaml", "frameworks/essential-eight-2023.json", ".gitignore"];

    public static IReadOnlyDictionary<string, string> Files(NewOrganisation org)
    {
        ArgumentNullException.ThrowIfNull(org);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hound.yaml"] = Config(org),
            ["scope/setup.yaml"] = Setup(org),
            ["scope/controls.yaml"] = Controls(org),
            [org.AuditFile] = Audit(org),
            ["README.md"] = Readme(org),
        };
    }

    private static string Config(NewOrganisation org) => Lines(
        $"# {org.Name}'s deployment config, written by hound init. Guide: docs/configuration.md in the Evidence Hound repo.",
        "# Check it with: hound config validate hound.yaml. Relative paths are relative to this file. Secrets are references only, never values.",
        "",
        "ciso_assistant:",
        $"  url: {Scalar(org.CisoAssistantUrl)}   # HOUND_CA_URL overrides this",
        "  token: env:HOUND_CA_TOKEN   # a personal access token for hound's service account",
        "",
        "evidence:",
        $"  root: {Scalar(org.EvidenceRoot)}   # outside the repo: evidence is never committed",
        "  retention_days: 90",
        "",
        "scheduler:",
        $"  time_zone: {Scalar(org.TimeZone)}",
        "",
        "scope: scope",
        "",
        "# Add one with: hound add collector <type> --config hound.yaml (hound collectors lists the types; manual is for exports people send in).",
        "collectors: []",
        "",
        "target:",
        "  mapping: mappings/essential-eight-2023.yaml",
        $"  compliance_assessment: {Scalar(org.AuditRef)}",
        $"  health_control: {HealthControl}");

    private static string Setup(NewOrganisation org) => Lines(
        $"# {org.Name}'s structure in CISO Assistant, written by hound init. Guide: docs/scope.md in the Evidence Hound repo.",
        "# Check it with: hound scope validate scope. Add systems with: hound add system --scope scope",
        "organisation:",
        "  root_folder: Global",
        "frameworks:",
        $"  - urn: {Framework}",
        $"    library: {Library}",
        "    snapshot: ../frameworks/essential-eight-2023.json",
        "domains:",
        $"  - name: {Scalar(org.Name)}",
        "    perimeter:",
        $"      name: {Perimeter}",
        $"      ref_id: {Scalar($"PER-{org.Code}")}");

    private static string Controls(NewOrganisation org) => Lines(
        "# Applied controls, declared once and used by any number of audits. Add one with: hound add control --scope scope",
        "# A status is set only when hound creates the control; after that, runs set it from the evidence.",
        "controls:",
        $"  - ref_id: {HealthControl}",
        "    name: Automated evidence collection",
        "    description: Evidence Hound collects evidence on a schedule; each collector's health is linked here.",
        $"    domain: {Scalar(org.Name)}",
        $"    owner: {Scalar(org.Owner)}",
        "    status: to_do",
        "    category: process");

    private static string Audit(NewOrganisation org) => Lines(
        $"# {org.Name}'s Essential Eight (November 2023) maturity level {org.Level.ToString(CultureInfo.InvariantCulture)} audit, by {org.Interval}, written by hound init.",
        "# Collectors publish their results to it. Write results no check covers under assessments; see docs/scope.md.",
        $"ref_id: {Scalar(org.AuditRef)}",
        $"name: {Scalar(string.Create(CultureInfo.InvariantCulture, $"Essential Eight ML{org.Level}"))}",
        $"framework: {Framework}",
        $"groups: [{string.Join(", ", Enumerable.Range(1, org.Level).Select(level => $"\"{level.ToString(CultureInfo.InvariantCulture)}\""))}]",
        $"domain: {Scalar(org.Name)}",
        $"perimeter: {Perimeter}",
        "# Each period is its own audit: <CODE>-E8-Q2-2027FY is October to December 2026, its ETA the period's last day. See docs/scope.md, Audit periods.",
        $"cycle: {{period: {org.Interval}, starts: \"{org.Starts.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\"}}",
        "systems: []",
        "documents: []",
        "controls: []",
        "assessments: []",
        "unlisted:",
        "  result: not_assessed");

    private static string Readme(NewOrganisation org) => Lines(
        $"# {org.Name} compliance",
        "",
        "Configuration for Evidence Hound and CISO Assistant, written by `hound init`. Keep this repository private: it describes the organisation.",
        "Secrets, evidence and exports never go in it (see `.gitignore`).",
        "",
        "Next steps:",
        "",
        "1. Add your systems: `hound add system --scope scope`",
        "2. Add the controls you rely on, linked to the audit's requirements: `hound add control --scope scope --audit " + org.AuditRef + "`",
        "3. Add collectors, such as a manual source for exports people send in: `hound add collector manual --config hound.yaml`",
        "4. Check everything offline: `hound config validate hound.yaml` and `hound scope validate scope --config hound.yaml`",
        "5. See what applying would do, then apply: `hound scope plan scope --config hound.yaml` and `hound scope apply scope --config hound.yaml`",
        "6. Run the collectors and see the result: `hound run --config hound.yaml` and `hound status --config hound.yaml`",
        "",
        "The Evidence Hound repo's `examples/compliance-repo` has a fuller example, with access, settings and a pull request workflow.");

    private static string Lines(params string[] lines) => string.Join('\n', lines) + "\n";
}
