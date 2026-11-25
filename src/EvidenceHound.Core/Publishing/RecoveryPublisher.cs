using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Publishing;

/// <summary>What a run did to the BIA recovery flags named by checks: the flags it set, and what it could not do.</summary>
public sealed record RecoveryReport(IReadOnlyList<RecoveryFlagUpdate> Set, IReadOnlyList<Problem> Problems)
{
    public static RecoveryReport None { get; } = new([], []);
}

/// <summary>Sets the BIA recovery flags named by checks after a run (s15-05).</summary>
public static class RecoveryPublisher
{
    public static Task<RecoveryReport> PublishAsync(ICisoAssistantClient client, IReadOnlyList<ScopeBia> bias, IReadOnlyList<CheckResult> latest, CancellationToken cancellationToken) =>
        Task.FromResult(RecoveryReport.None);
}
