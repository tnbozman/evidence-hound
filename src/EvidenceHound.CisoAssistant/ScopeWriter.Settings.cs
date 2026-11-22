using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>Feature flags and general settings, which are single objects in CISO Assistant rather than lists, and are never deleted.</summary>
internal sealed partial class ScopeWriter
{
    /// <summary>
    /// The flag endpoint validates the whole body and resets a flag the body leaves out to its default, so the write sends every flag as
    /// CISO Assistant holds it, with this one changed.
    /// </summary>
    private async Task<Result<Unit>> FeatureFlagAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        var value = write.Declared?.Fields.GetValueOrDefault("value");
        if (value is not ("true" or "false"))
        {
            return Result.Fail<Unit>(new Problem("scope.setting_not_deleted", $"hound never deletes feature flag {write.Change.Key}; set it to true or false."));
        }

        var held = await SendForJsonAsync(HttpMethod.Get, "settings/feature-flags/", null, "to read the feature flags", cancellationToken);
        return await held.BindAsync(async flags =>
        {
            var body = new JsonObject([.. (flags as JsonObject ?? []).Where(f => f.Value is JsonValue v && v.TryGetValue<bool>(out _))
                .Select(f => KeyValuePair.Create<string, JsonNode?>(f.Key, f.Value!.DeepClone()))]);
            body[write.Change.Key] = value == "true";
            return await SendAsync(HttpMethod.Put, "settings/feature-flags/", body, $"to set feature flag {write.Change.Key}", cancellationToken);
        });
    }

    /// <summary>The general settings endpoint keeps the settings a request leaves out, so the write sends this one alone.</summary>
    private Task<Result<Unit>> GeneralSettingAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        if (write.Declared?.Fields.GetValueOrDefault("value") is not { } json)
        {
            return Task.FromResult(Result.Fail<Unit>(new Problem("scope.setting_not_deleted", $"hound never deletes general setting {write.Change.Key}.")));
        }

        var body = new JsonObject { ["value"] = new JsonObject { [write.Change.Key] = JsonNode.Parse(json) } };
        return SendAsync(HttpMethod.Patch, "settings/general/", body, $"to set general setting {write.Change.Key}", cancellationToken);
    }
}
