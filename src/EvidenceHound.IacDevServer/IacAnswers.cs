using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace EvidenceHound.IacDevServer;

/// <summary>One workflow the dev server serves: its file, run and artefact.</summary>
public sealed record ServedWorkflow(string File, string Name, long RunId, long ArtifactId, string Artifact, string Member);

/// <summary>Turns an <see cref="IacDevPipeline"/> into the GitHub Actions answers, the JUnit report and the plan. Pure.</summary>
public static class IacAnswers
{
    public static readonly ServedWorkflow Controls = new(IacDevPipeline.ControlsWorkflow, "Control tests", 7101, 9101, "control-tests", "controls.xml");

    public static readonly ServedWorkflow Drift = new(IacDevPipeline.DriftWorkflow, "Drift", 7001, 9001, "drift-plan", "plan.json");

    public static readonly IReadOnlyList<ServedWorkflow> Workflows = [Controls, Drift];

    /// <summary>The newest completed run of <paramref name="workflow"/>: started two minutes before <paramref name="now"/>, finished one minute before.</summary>
    public static JsonObject Runs(IacDevPipeline pipeline, ServedWorkflow workflow, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(workflow);
        return new JsonObject
        {
            ["total_count"] = 1,
            ["workflow_runs"] = new JsonArray(new JsonObject
            {
                ["id"] = workflow.RunId,
                ["name"] = workflow.Name,
                ["run_number"] = 42,
                ["head_branch"] = "main",
                ["head_sha"] = pipeline.HeadSha,
                ["event"] = "schedule",
                ["status"] = "completed",
                ["conclusion"] = "success",
                ["created_at"] = Time(now.AddMinutes(-2)),
                ["updated_at"] = Time(now.AddMinutes(-1)),
                ["html_url"] = $"https://github.com/{pipeline.Repository}/actions/runs/{workflow.RunId}",
            }),
        };
    }

    public static JsonObject Artifacts(IacDevPipeline pipeline, ServedWorkflow workflow, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(workflow);
        return new JsonObject
        {
            ["total_count"] = 1,
            ["artifacts"] = new JsonArray(new JsonObject
            {
                ["id"] = workflow.ArtifactId,
                ["name"] = workflow.Artifact,
                ["size_in_bytes"] = 1024,
                ["expired"] = false,
                ["created_at"] = Time(now.AddMinutes(-1)),
                ["workflow_run"] = new JsonObject { ["id"] = workflow.RunId, ["head_branch"] = "main", ["head_sha"] = pipeline.HeadSha },
            }),
        };
    }

    /// <summary>The JUnit report <c>terraform test -junit-xml</c> writes, or the report the pipeline was given.</summary>
    public static string ControlsReport(IacDevPipeline pipeline, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline.GivenReport is { } given)
        {
            return given;
        }

        const string file = "tests/controls.tftest.hcl";
        var tests = pipeline.ControlTests;
        var suite = new XElement(
            "testsuite",
            new XAttribute("name", file),
            new XAttribute("tests", tests.Count),
            new XAttribute("skipped", 0),
            new XAttribute("failures", tests.Count(t => t.Failure is not null)),
            new XAttribute("errors", 0),
            tests.Select(t => new XElement(
                "testcase",
                new XAttribute("name", t.Name),
                new XAttribute("classname", file),
                new XAttribute("timestamp", Time(now.AddMinutes(-2))),
                t.Failure is { } message ? new XElement("failure", new XAttribute("message", "Test assertion failed"), message) : null)));
        var report = new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement("testsuites", suite));
        return report.Declaration + Environment.NewLine + report.Root;
    }

    /// <summary>The refresh-only plan as <c>terraform show -json</c> writes it: every resource a no-op, and the drift found.</summary>
    public static string Plan(IacDevPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return new JsonObject
        {
            ["format_version"] = "1.2",
            ["terraform_version"] = "1.9.8",
            ["resource_drift"] = new JsonArray([.. pipeline.Drift.Select(r => Change(r, "update"))]),
            ["resource_changes"] = new JsonArray([.. IacDevPipeline.Resources.Select(r => Change(r, "no-op"))]),
        }.ToJsonString();
    }

    /// <summary>The artefact's zip, as GitHub serves it: one member.</summary>
    public static byte[] Zip(string member, string content)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry(member).Open();
            entry.Write(Encoding.UTF8.GetBytes(content));
        }

        return stream.ToArray();
    }

    /// <summary>The content of <paramref name="workflow"/>'s artefact.</summary>
    public static string Member(IacDevPipeline pipeline, ServedWorkflow workflow, DateTimeOffset now) =>
        workflow == Controls ? ControlsReport(pipeline, now) : Plan(pipeline);

    private static JsonObject Change(DriftedResource resource, string action) => new()
    {
        ["address"] = resource.Address,
        ["type"] = resource.Type,
        ["change"] = new JsonObject { ["actions"] = new JsonArray(action) },
    };

    private static string Time(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
