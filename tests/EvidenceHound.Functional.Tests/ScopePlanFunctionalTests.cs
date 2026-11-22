using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s06-07 to s06-09: the fixture seeds CISO Assistant by applying the example scope with <c>hound scope apply</c>. That apply wrote every kind
/// of object; after it, <c>hound scope plan</c> lists no changes and a second apply writes nothing, even after the other functional tests
/// have published runs to the same audits. The seed's report, the plans and the second report land in <c>test-results/scope-plan/</c>,
/// which CI prints.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class ScopePlanFunctionalTests(CisoAssistantFixture ciso)
{
    [Fact]
    public async Task The_seed_applies_every_kind_then_plan_lists_no_changes_and_apply_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = CisoAssistantFixture.ExampleScope;
        var output = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "scope-plan")).FullName;

        var seed = JsonNode.Parse(ciso.SeedApplyReport)!;
        seed["failed"]!.GetValue<int>().ShouldBe(0, ciso.SeedApplyReport);
        var written = seed["changes"]!.AsArray()
            .Where(c => c!["outcome"]!.GetValue<string>() == "written")
            .Select(c => (Kind: c!["kind"]!.GetValue<string>(), Key: c["key"]!.GetValue<string>()))
            .ToArray();
        written.Select(c => c.Kind).Distinct().ShouldBe(
            ["feature_flag", "setting", "framework", "domain", "perimeter", "system", "user", "team", "role_assignment", "audit", "document", "control", "assessment"], ignoreOrder: true, ciso.SeedApplyReport);
        written.ShouldContain(("framework", "urn:example:risk:framework:example-baseline"), ciso.SeedApplyReport);
        written.ShouldContain(("audit", "EX-ESSENTIALS-2026"), ciso.SeedApplyReport);
        written.ShouldContain(("document", "identity-policy"), ciso.SeedApplyReport);
        written.ShouldContain(("control", "AC-AUTO-EVIDENCE"), ciso.SeedApplyReport);
        written.ShouldContain(("assessment", "EX-ESSENTIALS-2026/IT.GOV-01.1"), ciso.SeedApplyReport);
        written.ShouldContain(("assessment", "EX-E8-2026/ISM-1486"), ciso.SeedApplyReport);
        // s07-04: scope/access.yaml. The seed made it@ and ceo@ without names; auditor@ is new and gets CISO Assistant's invitation.
        written.ShouldContain(("user", "auditor@example.com"), ciso.SeedApplyReport);
        written.ShouldContain(("user", "ceo@example.com"), ciso.SeedApplyReport);
        written.ShouldContain(("team", "IT team"), ciso.SeedApplyReport);
        written.ShouldContain(("role_assignment", "BI-RL-APP|Example Pty Ltd|ceo@example.com"), ciso.SeedApplyReport);
        written.ShouldContain(("role_assignment", "BI-RL-ANA|Example Pty Ltd|it@example.com"), ciso.SeedApplyReport);
        // s07-05: scope/settings.yaml. dora is on by default and enforce_mfa off; nis2 and compliance already hold the declared value.
        written.ShouldContain(("feature_flag", "dora"), ciso.SeedApplyReport);
        written.ShouldContain(("setting", "enforce_mfa"), ciso.SeedApplyReport);
        written.ShouldNotContain(("feature_flag", "nis2"), ciso.SeedApplyReport);
        // CISO Assistant may load the mapping library itself when both its frameworks are loaded; either way it is loaded now.
        (await LoadedLibraryUrnsAsync(ct)).ShouldContain("urn:intuitem:risk:library:mapping-essential-eight-2023-and-asd-essentials-enterprise-it");

        var (jsonExit, json) = await PlanAsync(scope, ct, "--format", "json");
        var (markdownExit, markdown) = await PlanAsync(scope, ct);
        await File.WriteAllTextAsync(Path.Combine(output, "scope-plan.json"), json, ct);
        await File.WriteAllTextAsync(Path.Combine(output, "scope-plan.md"), markdown, ct);

        (jsonExit, markdownExit).ShouldBe((ExitCodes.Ok, ExitCodes.Ok), json + markdown);
        JsonNode.Parse(json)!["changes"]!.AsArray().ShouldBeEmpty(json);
        markdown.ShouldStartWith("## Scope plan\n\n0 to create, 0 to update, 0 to delete.");

        var (applyExit, apply) = await RunAsync("apply", scope, ct);
        await File.WriteAllTextAsync(Path.Combine(output, "second-apply.json"), apply, ct);

        applyExit.ShouldBe(ExitCodes.Ok, apply);
        var again = JsonNode.Parse(apply)!;
        (again["written"]!.GetValue<int>(), again["failed"]!.GetValue<int>()).ShouldBe((0, 0), apply);
        again["changes"]!.AsArray().ShouldBeEmpty(apply);
    }

    [Fact]
    public async Task A_flag_turned_on_in_settings_yaml_is_on_after_apply_and_off_again_when_the_file_says_so()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = CopyOfExampleScope();
        var settings = Path.Combine(scope, "settings.yaml");
        var original = await File.ReadAllTextAsync(settings, ct);

        foreach (var on in new[] { true, false })
        {
            await File.WriteAllTextAsync(settings, original.Replace("feature_flags:\n", $"feature_flags:\n  ttps: {(on ? "true" : "false")}\n", StringComparison.Ordinal), ct);
            var (applyExit, apply) = await RunAsync("apply", scope, ct);
            applyExit.ShouldBe(ExitCodes.Ok, apply);
            JsonNode.Parse(apply)!["failed"]!.GetValue<int>().ShouldBe(0, apply);
            (await FlagAsync("ttps", ct)).ShouldBe(on);

            var (planExit, plan) = await PlanAsync(scope, ct, "--format", "json");
            planExit.ShouldBe(ExitCodes.Ok, plan);
            JsonNode.Parse(plan)!["changes"]!.AsArray().ShouldBeEmpty(plan);
        }
    }

    [Fact]
    public async Task The_settings_snapshot_names_the_flags_and_general_settings_of_the_pinned_release()
    {
        var ct = TestContext.Current.CancellationToken;
        var snapshot = EvidenceHound.Core.Scope.CisoAssistantSettings.Current;

        var flags = (await GetAsync("settings/feature-flags/", ct)).AsObject().Where(p => p.Value is JsonValue v && v.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False);
        flags.Select(p => p.Key).ShouldBe(snapshot.FeatureFlags.Keys, ignoreOrder: true);
        var general = (await GetAsync("settings/general/", ct))["value"]!.AsObject().Select(p => p.Key);
        // The instance returns the settings it holds, which are some or all of the release's.
        general.Except(snapshot.SecretGeneral).ShouldBeSubsetOf(snapshot.General);
    }

    /// <summary>The example scope in a temporary folder, beside its frameworks, so a test can change a file without touching the repo.</summary>
    private static string CopyOfExampleScope()
    {
        var source = Directory.GetParent(CisoAssistantFixture.ExampleScope)!.FullName;
        var target = Path.Combine(Path.GetTempPath(), "hound-settings-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        return Path.Combine(target, Path.GetFileName(CisoAssistantFixture.ExampleScope));
    }

    private async Task<bool> FlagAsync(string name, CancellationToken ct) => (await GetAsync("settings/feature-flags/", ct))[name]!.GetValue<bool>();

    private async Task<JsonNode> GetAsync(string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new("Token", ciso.Token);
        return JsonNode.Parse(await http.GetStringAsync(new Uri(ciso.ApiUrl, path), ct))!;
    }

    private async Task<List<string>> LoadedLibraryUrnsAsync(CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, "loaded-libraries/?limit=1000"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        return [.. (body is JsonObject page ? page["results"]! : body).AsArray().Select(l => l!["urn"]!.GetValue<string>())];
    }

    private Task<(int ExitCode, string Output)> PlanAsync(string scope, CancellationToken ct, params string[] args) => RunAsync("plan", scope, ct, args);

    private async Task<(int ExitCode, string Output)> RunAsync(string command, string scope, CancellationToken ct, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            ["scope", command, scope, .. args],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        // Logs go to the error writer; on a failure they are what explains it.
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : output.ToString() + error.ToString());
    }
}
