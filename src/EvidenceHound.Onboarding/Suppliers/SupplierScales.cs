using System.Globalization;
using System.Text;

namespace EvidenceHound.Onboarding.Suppliers;

/// <summary>One value of a scale and what it means.</summary>
public sealed record ScaleLine(int Value, string Meaning);

/// <summary>A supplier level's scale: what the level measures, and a line for each value.</summary>
public sealed record SupplierScale(string Name, string About, IReadOnlyList<ScaleLine> Lines);

/// <summary>
/// What every value of every supplier level means, in one table for the terminal and the guide. Dependency, penetration, maturity and trust
/// follow EBIOS Risk Manager's stakeholder criteria, which CISO Assistant's third-party records use; criticality is the solution's. Pure.
/// </summary>
public static class SupplierScales
{
    private static readonly IReadOnlyList<SupplierScale> Table =
    [
        new("dependency", "how much the system relies on this supplier to keep working.",
        [
            new(0, "None: nothing stops if the supplier does."),
            new(1, "Low: the supplier is useful, and work carries on without it."),
            new(2, "Moderate: work is harder without it, and another supplier could step in."),
            new(3, "High: the supplier is needed, though it could be replaced with effort."),
            new(4, "Total: the supplier is needed, and nothing could replace it in the short term."),
        ]),
        new("penetration", "how far the supplier can reach into your systems and data.",
        [
            new(0, "None: no access to your systems or data."),
            new(1, "Access as an ordinary user, or to user devices only."),
            new(2, "Administrator access to user devices, or physical access to your premises."),
            new(3, "Administrator access to business systems or servers."),
            new(4, "Administrator access to core infrastructure, or physical access to server rooms."),
        ]),
        new("maturity", "how well the supplier manages its own cyber security.",
        [
            new(1, "Security is handled case by case, with nothing written down."),
            new(2, "Good practice and regulation are followed, without one overall policy."),
            new(3, "An overall security policy is applied, and problems are mostly handled as they arise."),
            new(4, "Risk is managed under an overall policy, ahead of problems."),
        ]),
        new("trust", "how sure you are that the supplier's intentions and interests line up with yours.",
        [
            new(1, "Its intentions cannot be judged."),
            new(2, "Its intentions are taken to be neutral."),
            new(3, "Its intentions are known and probably good."),
            new(4, "Its intentions are known and fully in line with yours."),
        ]),
        new("criticality", "how much the business depends on what the supplier provides (the solution).",
        [
            new(1, "Low: the business carries on without it."),
            new(2, "Medium: some work is slower or harder without it."),
            new(3, "High: important work stops without it."),
            new(4, "Critical: the business cannot run without it."),
        ]),
    ];

    public static SupplierScale For(string level) => Table.First(s => s.Name == level);

    /// <summary>The scale as the terminal prints it before the level is asked: what it measures, a line per value, and what an empty answer means.</summary>
    public static string Describe(string level)
    {
        var scale = For(level);
        var low = scale.Lines[0].Value;
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"{level} ({low} to {scale.Lines[^1].Value}): {scale.About}\n");
        foreach (var line in scale.Lines)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {line.Value}  {line.Meaning}\n");
        }

        return text.Append(low > SupplierProposal.NotYetAssessed
            ? "  Leave it empty when it is not yet assessed; the supplier file keeps it to decide.\n"
            : "  Leave it empty to decide later in the supplier file.\n").ToString();
    }
}
