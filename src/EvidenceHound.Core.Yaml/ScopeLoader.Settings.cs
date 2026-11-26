using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads <c>settings.yaml</c>: feature flags and general settings.</summary>
public static partial class ScopeLoader
{
    private static readonly string[] SettingsKeys = ["feature_flags", "general"];

    /// <summary>Feature flags as true or false, and general settings as any YAML value, kept as JSON. Names are checked by validation.</summary>
    private static ScopeSettings ReadSettings(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, SettingsKeys);
        var flags = file.Map(root, "feature_flags") is { } flagMap
            ? flagMap.Children.Select(pair => ReadFlag(pair.Key, pair.Value, file)).OfType<ScopeFlag>().ToArray()
            : [];
        var general = file.Map(root, "general") is { } generalMap ? generalMap : null;
        var secrets = general is null ? [] : CisoAssistantSettings.Current.SecretGeneral.Concat(SecretKeys).ToArray();
        if (general is not null)
        {
            file.Secrets(general, secrets);
        }

        return new ScopeSettings(
            flags,
            general is null
                ? []
                : [.. general.Children
                    .Where(pair => pair.Key is YamlScalarNode { Value: { } name } && !secrets.Contains(name, StringComparer.Ordinal))
                    .Select(pair => new ScopeGeneralSetting(((YamlScalarNode)pair.Key).Value!, CisoAssistantSettings.Json(ToJson(pair.Value)), file.At(pair.Key)))]);
    }

    private static ScopeFlag? ReadFlag(YamlNode key, YamlNode value, FileReader file)
    {
        if (key is not YamlScalarNode { Value: { } name })
        {
            return null;
        }

        if (value is YamlScalarNode { Style: ScalarStyle.Plain, Value: "true" or "false" } flag)
        {
            return new ScopeFlag(name, flag.Value == "true", file.At(key));
        }

        file.Invalid(value, $"feature flag '{name}' must be true or false.");
        return null;
    }

    /// <summary>A YAML value as JSON: unquoted true, false, numbers and null keep their type; everything else is text.</summary>
    private static JsonNode? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(c => KeyValuePair.Create(((YamlScalarNode)c.Key).Value ?? "", ToJson(c.Value)))),
        YamlSequenceNode list => new JsonArray([.. list.Children.Select(ToJson)]),
        YamlScalarNode { Style: ScalarStyle.Plain, Value: "true" or "false" } scalar => JsonValue.Create(scalar.Value == "true"),
        YamlScalarNode { Style: ScalarStyle.Plain, Value: null or "" or "null" or "~" } => null,
        YamlScalarNode { Style: ScalarStyle.Plain, Value: { } text } when long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole) => JsonValue.Create(whole),
        YamlScalarNode { Style: ScalarStyle.Plain, Value: { } text } when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => JsonValue.Create(number),
        YamlScalarNode scalar => JsonValue.Create(scalar.Value),
        _ => null,
    };
}
