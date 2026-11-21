using System.Reflection;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Core.Tests.Checks;

public class FrameworkNeutralityTests
{
    [Fact]
    public void No_check_type_mentions_a_framework()
    {
        string[] frameworkWords = ["essential", "eight", "e8", "nist", "800", "iso", "27001", "cmmc", "requirement", "framework"];
        var names = typeof(CheckId).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(CheckId).Namespace)
            .SelectMany(t => new[] { t.Name }.Concat(t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name)))
            .Select(n => n.ToUpperInvariant());

        names.ShouldNotContain(n => frameworkWords.Any(w => n.Contains(w.ToUpperInvariant(), StringComparison.Ordinal)));
    }
}
