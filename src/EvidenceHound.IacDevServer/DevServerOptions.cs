using System.Globalization;

namespace EvidenceHound.IacDevServer;

/// <summary>
/// The dev server's command line: <c>--variant compliant|gaps</c>, <c>--deviation &lt;name&gt;</c> (repeatable), <c>--controls-report
/// &lt;file&gt;</c> (a JUnit report from <c>terraform test</c> to serve), <c>--port &lt;n&gt;</c> and <c>--public-url &lt;url&gt;</c>.
/// <see cref="Parse"/> returns the options, or a message.
/// </summary>
public sealed record DevServerOptions(int Port, Uri PublicUrl, string Variant, IReadOnlyList<string> Deviations, string? ControlsReport)
{
    public static readonly int DefaultPort = 5380;

    /// <summary>The pipeline these options describe; the report file, when given, is read by the caller.</summary>
    public IacDevPipeline Pipeline =>
        IacDevPipeline.Deviations.Where(d => Deviations.Contains(d.Name)).Aggregate(IacDevPipeline.Compliant, (p, d) => p.With(d));

    public static object Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var port = DefaultPort;
        Uri? publicUrl = null;
        string? report = null;
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
                case "--deviation" when IacDevPipeline.Deviations.Any(d => d.Name == value):
                    named.Add(value);
                    break;
                case "--controls-report" when !string.IsNullOrWhiteSpace(value):
                    report = value;
                    break;
                case "--port":
                    return $"--port needs a port number, not '{value}'.";
                case "--public-url":
                    return $"--public-url needs an http or https address, not '{value}'.";
                case "--variant":
                    return $"--variant is compliant or gaps, not '{value}'.";
                case "--deviation":
                    return $"--deviation is one of {string.Join(", ", IacDevPipeline.Deviations.Select(d => d.Name))}; '{value}' is not.";
                case "--controls-report":
                    return "--controls-report needs the path of a JUnit report from terraform test -junit-xml.";
                default:
                    return $"Unknown option {option}. Use --variant, --deviation, --controls-report, --port or --public-url.";
            }
        }

        var names = variant == "gaps" ? IacDevPipeline.Deviations.Select(d => d.Name).ToList() : named.Distinct(StringComparer.Ordinal).ToList();
        return new DevServerOptions(port, publicUrl ?? new Uri(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{port}/")), variant, names, report);
    }
}
