using System.Text.RegularExpressions;

namespace EvidenceHound.Cli.Setup;

/// <summary>
/// s18-08: one <c>hound.yaml</c> target per audit lets one run feed every audit. An audit of one system that a person approves gets its
/// target when the repo has that system's own mapping of the framework (<c>mappings/&lt;system&gt;-*.yaml</c>), so it is fed without a hand
/// edit. The framework's general mapping is never used for it: it maps every collector's checks, so another system's results would land in
/// this system's audit.
/// </summary>
internal static partial class AuditTargets
{
    /// <summary>What adding a target did: whether <c>hound.yaml</c> changed, and a line for the person.</summary>
    internal sealed record Added(bool Written, string Note);

    /// <summary>Adds the audit's target to the repo's <c>hound.yaml</c>; null when the repo has no config.</summary>
    public static async Task<Added?> AddAsync(string repo, string auditRef, string frameworkUrn, IReadOnlyList<string> systems, CancellationToken cancellationToken)
    {
        var config = Path.Combine(repo, "hound.yaml");
        if (!File.Exists(config))
        {
            return null;
        }

        var yaml = await File.ReadAllTextAsync(config, cancellationToken);
        if (Targeted().Matches(yaml).Any(m => m.Groups["audit"].Value == auditRef))
        {
            return new Added(false, $"hound.yaml already publishes to {auditRef}.");
        }

        if (systems is not [var system])
        {
            return new Added(false, $"hound.yaml does not publish to {auditRef} yet: add a target for it with the mapping of the checks that answer it.");
        }

        var prefix = $"{OnboardingSlug(system)}-";
        if (await MappingAsync(repo, prefix, frameworkUrn, cancellationToken) is not { } mapping)
        {
            return new Added(false, $"No mapping of the checks for {system} (mappings/{prefix}<framework>.yaml), so hound.yaml does not publish to {auditRef} yet: add one, then a target for it.");
        }

        var item = YamlMap.Of(("mapping", new YamlText(mapping)), ("compliance_assessment", new YamlText(auditRef)));
        return await YamlEdit.AppendItem(yaml, "targets", item).Match<Task<Added>>(
            async edited =>
            {
                await File.WriteAllTextAsync(config, edited, cancellationToken);
                return new Added(true, $"hound.yaml now publishes to {auditRef}, with {mapping}.");
            },
            problems => Task.FromResult(new Added(false, $"Add a target for {auditRef} to hound.yaml by hand: {string.Join(" ", problems.Select(p => p.Message))}")));
    }

    /// <summary>The first mapping under <c>mappings/</c> named for the system, by name, whose <c>framework_urn</c> is the audit's framework.</summary>
    private static async Task<string?> MappingAsync(string repo, string prefix, string frameworkUrn, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(repo, "mappings");
        if (!Directory.Exists(folder))
        {
            return null;
        }

        foreach (var file in Directory.GetFiles(folder, $"{prefix}*.yaml").Order(StringComparer.Ordinal))
        {
            if (FrameworkUrn().Match(await File.ReadAllTextAsync(file, cancellationToken)) is { Success: true } urn && urn.Groups["urn"].Value == frameworkUrn)
            {
                return $"mappings/{Path.GetFileName(file)}";
            }
        }

        return null;
    }

    private static string OnboardingSlug(string system) => Wizard.Slug(system).ToLowerInvariant();

    [GeneratedRegex(@"^\s*-?\s*compliance_assessment:\s*""?(?<audit>[^""\s#]+)", RegexOptions.Multiline)]
    private static partial Regex Targeted();

    [GeneratedRegex(@"^framework_urn:\s*""?(?<urn>[^""\s#]+)", RegexOptions.Multiline)]
    private static partial Regex FrameworkUrn();
}
