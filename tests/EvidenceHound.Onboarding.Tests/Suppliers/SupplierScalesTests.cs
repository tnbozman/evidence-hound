using EvidenceHound.Onboarding.Suppliers;

namespace EvidenceHound.Onboarding.Tests.Suppliers;

/// <summary>s09-35: one table says what every value of every supplier level means, for the terminal and the guide.</summary>
public class SupplierScalesTests
{
    [Fact]
    public void Every_level_has_a_line_per_value()
    {
        foreach (var (name, low, high) in SupplierProposal.Scales)
        {
            var scale = SupplierScales.For(name);

            scale.About.ShouldNotBeNullOrWhiteSpace(name);
            scale.Lines.Select(l => l.Value).ShouldBe(Enumerable.Range(low, high - low + 1), name);
            scale.Lines.ShouldAllBe(l => l.Meaning.Length > 10);
            var shown = SupplierScales.Describe(name);
            shown.ShouldStartWith($"{name} ({low} to {high}): {scale.About}");
            foreach (var line in scale.Lines)
            {
                shown.ShouldContain($"  {line.Value}  {line.Meaning}");
            }

            // A level on a 1 to 4 scale can be left not yet assessed; 0 is a real value on the others.
            shown.Contains("not yet assessed", StringComparison.Ordinal).ShouldBe(low > SupplierProposal.NotYetAssessed, name);
        }
    }
}
