using System.Globalization;

namespace EvidenceHound.ScanDev;

/// <summary>
/// The command line: <c>--variant compliant|gaps</c>, <c>--deviation &lt;name&gt;</c> (repeatable), <c>--out &lt;folder&gt;</c> and
/// <c>--today yyyy-MM-dd</c>. <see cref="Parse"/> returns the options, or a message.
/// </summary>
public sealed record ScanDevOptions(string Out, DateOnly Today, string Variant, IReadOnlyList<string> Deviations, ScanDevEstate Estate)
{
    public static readonly string DefaultOut = ".dev/scans";

    public static object Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var output = DefaultOut;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var variant = "compliant";
        var named = new List<string>();
        for (var i = 0; i < args.Count; i += 2)
        {
            var option = args[i];
            var value = i + 1 < args.Count ? args[i + 1] : string.Empty;
            switch (option)
            {
                case "--out" when value.Length > 0:
                    output = value;
                    break;
                case "--today" when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day):
                    today = day;
                    break;
                case "--variant" when value is "compliant" or "gaps":
                    variant = value;
                    break;
                case "--deviation" when ScanDevEstate.Deviations.Any(d => d.Name == value):
                    named.Add(value);
                    break;
                case "--out":
                    return "--out needs a folder.";
                case "--today":
                    return $"--today needs a date as yyyy-MM-dd, not '{value}'.";
                case "--variant":
                    return $"--variant is compliant or gaps, not '{value}'.";
                case "--deviation":
                    return $"--deviation is one of {string.Join(", ", ScanDevEstate.Deviations.Select(d => d.Name))}; '{value}' is not.";
                default:
                    return $"Unknown option {option}. Use --variant, --deviation, --out or --today.";
            }
        }

        var names = variant == "gaps" ? ScanDevEstate.Deviations.Select(d => d.Name).ToList() : named.Distinct(StringComparer.Ordinal).ToList();
        var estate = ScanDevEstate.Deviations.Where(d => names.Contains(d.Name)).Aggregate(ScanDevEstate.Compliant, (e, d) => e.With(d));
        return new ScanDevOptions(output, today, variant, names, estate);
    }
}
