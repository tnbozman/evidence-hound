using System.Globalization;

namespace EvidenceHound.SophosDevServer;

/// <summary>
/// The dev server's command line: <c>--variant compliant|gaps</c>, <c>--deviation &lt;name&gt;</c> (repeatable), <c>--port &lt;n&gt;</c> and
/// <c>--public-url &lt;url&gt;</c>, the address the collector reaches the server at. <see cref="Parse"/> returns the options, or a message.
/// </summary>
public sealed record DevServerOptions(int Port, Uri PublicUrl, string Variant, IReadOnlyList<string> Deviations, SophosDevTenant Tenant)
{
    public static readonly int DefaultPort = 5370;

    public static object Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var port = DefaultPort;
        Uri? publicUrl = null;
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
                case "--public-url" when Uri.TryCreate(value.EndsWith('/') ? value : value + "/", UriKind.Absolute, out var url) && url.Scheme is "http" or "https":
                    publicUrl = url;
                    break;
                case "--variant" when value is "compliant" or "gaps":
                    variant = value;
                    break;
                case "--deviation" when SophosDevTenant.Deviations.Any(d => d.Name == value):
                    named.Add(value);
                    break;
                case "--port":
                    return $"--port needs a port number, not '{value}'.";
                case "--public-url":
                    return $"--public-url needs an http or https address, not '{value}'.";
                case "--variant":
                    return $"--variant is compliant or gaps, not '{value}'.";
                case "--deviation":
                    return $"--deviation is one of {string.Join(", ", SophosDevTenant.Deviations.Select(d => d.Name))}; '{value}' is not.";
                default:
                    return $"Unknown option {option}. Use --variant, --deviation, --port or --public-url.";
            }
        }

        var names = variant == "gaps" ? SophosDevTenant.Deviations.Select(d => d.Name).ToList() : named.Distinct(StringComparer.Ordinal).ToList();
        var tenant = SophosDevTenant.Deviations.Where(d => names.Contains(d.Name)).Aggregate(SophosDevTenant.Compliant, (t, d) => t.With(d));
        return new DevServerOptions(port, publicUrl ?? new Uri(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{port}/")), variant, names, tenant);
    }
}
