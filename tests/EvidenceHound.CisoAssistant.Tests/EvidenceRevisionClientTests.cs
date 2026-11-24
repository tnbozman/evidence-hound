using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>
/// s11-05: <c>hound ssp publish</c> reads CISO Assistant's own Word report for the audit, and uploads the signed plan as a new revision of
/// the programme's "System security plan" evidence item.
/// </summary>
public sealed class EvidenceRevisionClientTests : IDisposable
{
    private static readonly byte[] Report = [0x50, 0x4b, 0x03, 0x04, 0x01, 0x02];

    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Downloads_the_audit_word_report()
    {
        Respond("GET", "/api/compliance-assessments/", Page(new JsonObject { ["id"] = "ca-1", ["ref_id"] = "EX-E8-2026" }));
        _ca.Server.Given(Request.Create().WithPath("/api/compliance-assessments/ca-1/word_report/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/vnd.openxmlformats-officedocument.wordprocessingml.document").WithBody(Report));

        var report = (await _ca.Client.DownloadAuditReportAsync("EX-E8-2026", _ct)).Value();

        report.FileName.ShouldBe("EX-E8-2026-audit-report.docx");
        report.Content.ToArray().ShouldBe(Report);
    }

    [Fact]
    public async Task A_missing_audit_report_is_a_problem()
    {
        Respond("GET", "/api/compliance-assessments/", Page(new JsonObject { ["id"] = "ca-1", ["ref_id"] = "EX-E8-2026" }));
        _ca.Server.Given(Request.Create().WithPath("/api/compliance-assessments/ca-1/word_report/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("boom"));

        var result = await _ca.Client.DownloadAuditReportAsync("EX-E8-2026", _ct);

        result.ShouldBeOfType<Result<EvidenceFile>.Fail>();
    }

    [Fact]
    public async Task Uploads_ssp_as_new_revision()
    {
        Respond("GET", "/api/evidences/", Page(new JsonObject { ["id"] = "ev-ssp", ["ref_id"] = "SSP-ESSENTIAL-EIGHT", ["name"] = "System security plan: Example corporate IT" }));
        Respond("GET", "/api/evidence-revisions/", Page(new JsonObject { ["id"] = "rev-1", ["version"] = 1, ["attachment_hash"] = "old" }));
        Respond("POST", "/api/evidence-revisions/", new JsonObject { ["id"] = "rev-2" }.ToJsonString());

        var evidence = (await _ca.Client.FindOrCreateEvidenceAsync("SSP-ESSENTIAL-EIGHT", "System security plan: Example corporate IT", _ct)).Value();
        var uploaded = await _ca.Client.UploadEvidenceRevisionAsync(evidence.Id, new EvidenceFile("ssp-essential-eight-1.0.zip", Encoding.UTF8.GetBytes("zip")), _ct);

        uploaded.ShouldBe(Result.Ok(Unit.Value));
        var post = _ca.Server.LogEntries.Select(e => e.RequestMessage!).Single(r => r.Method == "POST");
        post.Path.ShouldBe("/api/evidence-revisions/");
        post.Body!.ShouldContain("ev-ssp");
        post.Body!.ShouldContain("filename=ssp-essential-eight-1.0.zip");
        _ca.Server.LogEntries.ShouldNotContain(e => e.RequestMessage!.Path == "/api/evidences/" && e.RequestMessage.Method == "POST");
    }

    private static string Page(params JsonObject[] results) =>
        new JsonObject { ["count"] = results.Length, ["next"] = null, ["results"] = new JsonArray(results) }.ToJsonString();

    private void Respond(string method, string path, string body) =>
        _ca.Server.Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body));
}
