namespace EvidenceHound.Functional.Tests;

/// <summary>
/// The fictional risk library under its own name for one test. CISO Assistant refuses a library whose matrix or threats reuse a
/// <c>ref_id</c> or name already loaded in the same folder, and the functional tests share one instance, so each copy renames them too.
/// </summary>
internal static class ExampleRiskLibrary
{
    /// <summary>The library with URNs <c>example-{name}</c>, matrix <c>example-{name}-4x4</c> and threats <c>{NAME}-THR-01</c> and so on.</summary>
    public static async Task<string> CopyAsync(string name, CancellationToken ct)
    {
        var prefix = name.ToUpperInvariant();
        return (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "files", "frameworks", "example-risk-library.yaml"), ct))
            .Replace("example-risk", $"example-{name}", StringComparison.Ordinal)
            .Replace("example-4x4", $"example-{name}-4x4", StringComparison.Ordinal)
            .Replace("name: Example 4x4", $"name: Example {name} 4x4", StringComparison.Ordinal)
            .Replace("name: Example Pty Ltd risk matrix and threats", $"name: Example Pty Ltd {name} risk matrix and threats", StringComparison.Ordinal)
            .Replace("ref_id: THR-", $"ref_id: {prefix}-THR-", StringComparison.Ordinal)
            .Replace("name: Loss of the only backup administrator", $"name: Loss of the only backup administrator ({name})", StringComparison.Ordinal)
            .Replace("name: Supplier remote access misused", $"name: Supplier remote access misused ({name})", StringComparison.Ordinal);
    }
}
