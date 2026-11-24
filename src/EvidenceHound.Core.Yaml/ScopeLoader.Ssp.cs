using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary><c>scope/ssp.yaml</c>: the system security plans, one per programme (s11-02).</summary>
public static partial class ScopeLoader
{
    private static readonly string[] SspFileKeys = ["ssps"];
    private static readonly string[] SspKeys = ["programme", "profile", "system_name", "perimeter", "version", "classification", "operational_status", "system_type", "roles", "documents", "sections"];
    private static readonly string[] SspProfiles = [.. SspProfile.Names];
    private static readonly string[] SspRoleNames = [.. SspProfile.Roles];

    private static IReadOnlyList<ScopeSsp> ReadSsps(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, SspFileKeys);
        return [.. file.Items(root, "ssps").Select(item => ReadSsp(item, file)).OfType<ScopeSsp>()];
    }

    private static ScopeSsp? ReadSsp(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, SspKeys);
        var programme = file.Ref(item, "programme", required: true);
        var profile = file.OneOf(item, "profile", SspProfiles, required: true);
        var systemName = file.Required(item, "system_name");
        var perimeter = file.Ref(item, "perimeter", required: true);
        var version = file.Required(item, "version");
        var roles = file.Map(item, "roles");
        if (roles is not null)
        {
            file.Keys(roles, SspRoleNames);
        }

        return programme is null || profile is null || systemName is null || perimeter is null || version is null
            ? null
            : new ScopeSsp(
                programme,
                profile,
                systemName,
                perimeter,
                version,
                roles is null ? [] : [.. SspRoleNames.Select(r => file.Ref(roles, r) is { } holder ? new SspRole(r, holder) : null).OfType<SspRole>()],
                file.Refs(item, "documents"),
                file.Map(item, "sections") is { } sections
                    ? [.. sections.Children.Where(c => c.Key is YamlScalarNode { Value: not null } && c.Value is YamlScalarNode { Value: not null })
                        .Select(c => new SspSection(((YamlScalarNode)c.Key).Value!, ((YamlScalarNode)c.Value).Value!, file.At(c.Key)))]
                    : [],
                file.At(item));
    }
}
