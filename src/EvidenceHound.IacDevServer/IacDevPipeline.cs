using EvidenceHound.Core.Checks;

namespace EvidenceHound.IacDevServer;

/// <summary>One run block of the control tests, and its failure message when it fails.</summary>
public sealed record ControlTest(string Name, string? Failure = null);

/// <summary>A resource the drift plan reports as changed outside Terraform.</summary>
public sealed record DriftedResource(string Address, string Type);

/// <summary>A named change to the pipeline, the checks it fails and how it changes the pipeline.</summary>
public sealed record Deviation(string Name, string Description, IReadOnlyDictionary<string, CheckOutcome> Fails, Func<IacDevPipeline, IacDevPipeline> Apply);

/// <summary>The fictional Example Pty Ltd IaC pipeline.</summary>
public sealed record IacDevPipeline(string Repository, string HeadSha, IReadOnlyList<ControlTest> ControlTests, IReadOnlyList<DriftedResource> Drift, string? GivenReport = null)
{
    public static readonly string Token = "iac-dev-token-not-a-secret";

    public static IacDevPipeline Compliant => throw new NotImplementedException();

    public static IReadOnlyList<Deviation> Deviations => throw new NotImplementedException();

    public static Dictionary<string, string> Settings(Uri url, string profile) => throw new NotImplementedException();

    public IacDevPipeline With(Deviation deviation) => throw new NotImplementedException();

    public IacDevPipeline WithReport(string report) => throw new NotImplementedException();
}
