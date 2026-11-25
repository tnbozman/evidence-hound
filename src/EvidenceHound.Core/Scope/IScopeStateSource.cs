using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>What to read: the root folder, the frameworks and other libraries the scope loads, and the rows of each audit the scope writes.</summary>
public sealed record ScopeStateQuery(string RootFolder, IReadOnlyList<string> FrameworkUrns, IReadOnlyDictionary<string, IReadOnlyList<RequirementKey>> Rows)
{
    /// <summary>Whether to read users, teams and role assignments: only when the scope declares access.</summary>
    public bool Access { get; init; }

    /// <summary>Whether to read feature flags and general settings: when the scope declares settings, or to list the unmanaged ones.</summary>
    public bool Settings { get; init; }

    /// <summary>Whether to read entities, solutions, contracts and representatives: only when the scope declares third parties.</summary>
    public bool ThirdParties { get; init; }

    /// <summary>Whether to read the evidence plan's recurring task templates: only when the scope declares an evidence plan.</summary>
    public bool EvidencePlan { get; init; }

    /// <summary>Whether to read risk studies, their scenarios, security exceptions and risk acceptances: only when the scope declares studies or exceptions (s15-02, s15-03).</summary>
    public bool Risks { get; init; }

    /// <summary>Whether to read business impact analyses, their asset assessments and thresholds: only when the scope declares BIAs (s15-04).</summary>
    public bool Bias { get; init; }

    /// <summary>Whether to read the organisation's own entity (the builtin main entity): only when setup declares it.</summary>
    public bool Organisation { get; init; }

    /// <summary>
    /// The custom libraries (s15-01) whose stored file hash to read, so a change without a version bump is caught: the library URN by the key of
    /// the object that loads it (a framework's URN, or the library's own).
    /// </summary>
    public IReadOnlyDictionary<string, string> CustomLibraries { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The audits, by ref_id, to read every row of, not only the rows the scope writes, so a plan can show its effect on compliance. Rows the
    /// scope does not declare are never written.
    /// </summary>
    public IReadOnlySet<string> AllRowsOf { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The query for a scope: its root folder, its frameworks and other libraries, and the rows its <paramref name="declared"/> objects write.</summary>
    public static ScopeStateQuery For(ScopeDocument scope, IReadOnlyList<ScopeObject> declared)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(declared);
        return new(
            scope.Setup.RootFolder,
            [.. scope.Setup.Frameworks.Select(f => f.Urn), .. scope.Setup.Libraries.Select(l => l.Value), .. scope.Setup.LibraryFiles.Select(l => l.Urn)],
            declared.Where(d => d.Kind == ScopeKind.Assessment)
                .Select(d => d.Key.Split('/', 2))
                .Where(parts => parts.Length == 2)
                .GroupBy(parts => parts[0], StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<RequirementKey>)[.. g.Select(parts => parts[1].StartsWith("urn:", StringComparison.Ordinal) ? RequirementKey.Urn(parts[1]) : RequirementKey.Ref(parts[1]))],
                    StringComparer.Ordinal))
        {
            Access = scope.Access.Users.Count > 0 || scope.Access.Teams.Count > 0 || scope.Access.Roles.Count > 0,
            Settings = scope.Settings.FeatureFlags.Count > 0 || scope.Settings.General.Count > 0,
            ThirdParties = scope.ThirdParties.Count > 0,
            Organisation = scope.Setup.Organisation is not null,
            EvidencePlan = scope.EvidencePlan is not null,
            Risks = scope.RiskStudies.Count > 0 || scope.Exceptions.Count > 0,
            Bias = scope.Bias.Count > 0,
            CustomLibraries = declared.Where(d => d.Kind == ScopeKind.Framework && d.Files.Count > 0 && d.Initial.ContainsKey("library"))
                .GroupBy(d => d.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Initial["library"], StringComparer.Ordinal),
            AllRowsOf = declared.Where(d => d.Kind == ScopeKind.Audit).Select(d => d.Key).ToHashSet(StringComparer.Ordinal),
        };
    }
}

/// <summary>Reads what CISO Assistant holds under the root folder, as objects the plan can compare with the scope. Read-only.</summary>
public interface IScopeStateSource
{
    Task<Result<IReadOnlyList<ActualObject>>> ReadAsync(ScopeStateQuery query, CancellationToken cancellationToken);

    /// <summary>Every user with whether MFA is on and every role they hold, directly or through a user group, as "role on domain".</summary>
    Task<Result<IReadOnlyList<UserAccess>>> ReadAccessAsync(CancellationToken cancellationToken);
}

/// <summary>A user as CISO Assistant holds them: whether MFA is on, and their roles as "BI-RL-APP on Example Pty Ltd".</summary>
public sealed record UserAccess(string Email, bool MfaEnabled, IReadOnlyList<string> Roles);
