using EvidenceHound.Collectors;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Collectors;

/// <summary>The built-in <c>ai-register</c> collector (s11-13).</summary>
public sealed class AiRegisterCollector : ICollector
{
    public const string ScopeSetting = "scope";

    public static IReadOnlyList<string> Checks { get; } = [];

    public string Type => "ai-register";

    public string Version => "1.0.0";

    public CollectorDescription Describe() => new([], [], []);

    public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Fail<CollectionResult>(new Problem("todo", "todo")));
}
