using System.Globalization;

namespace EvidenceHound.M365DevServer;

/// <summary>
/// The dev server's command line: <c>--variant compliant|gaps</c>, <c>--deviation &lt;name&gt;</c> (repeatable, on top of the variant)
/// and <c>--port &lt;n&gt;</c>. <see cref="Parse"/> returns the options, or a message naming what it refused.
/// </summary>
public sealed record DevServerOptions(int Port, string Variant, IReadOnlyList<string> Deviations, DevTenant Tenant)
{
    public static readonly int DefaultPort = 5365;

    public static object Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var port = DefaultPort;
        var variant = "compliant";
        var named = new List<string>();
        for (var i = 0; i < args.Count; i += 2)
        {
            var option = args[i];
            var value = i + 1 < args.Count ? args[i + 1] : string.Empty;
            switch (option)
            {
                case "--port" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is > 0 and < 65536:
                    port = number;
                    break;
                case "--variant" when value is "compliant" or "gaps":
                    variant = value;
                    break;
                case "--deviation" when DevTenant.Deviations.Any(d => d.Name == value):
                    named.Add(value);
                    break;
                case "--port":
                    return $"--port needs a port number, not '{value}'.";
                case "--variant":
                    return $"--variant is compliant or gaps, not '{value}'.";
                case "--deviation":
                    return $"--deviation is one of {string.Join(", ", DevTenant.Deviations.Select(d => d.Name))}; '{value}' is not.";
                default:
                    return $"Unknown option {option}. Use --variant, --deviation or --port.";
            }
        }

        var names = variant == "gaps" ? DevTenant.Deviations.Select(d => d.Name).ToList() : named.Distinct(StringComparer.Ordinal).ToList();
        var tenant = DevTenant.Deviations.Where(d => names.Contains(d.Name)).Aggregate(DevTenant.Compliant, (t, d) => t.With(d));
        return new DevServerOptions(port, variant, names, tenant);
    }
}
