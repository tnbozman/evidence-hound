using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary><c>scope/irp.yaml</c>: the incident response plan's programme, roles and hand-written sections (s15-09).</summary>
public static partial class ScopeLoader
{
    private static readonly string[] IrpKeys = ["programme", "version", "roles", "sections"];

    /// <summary>Only <c>scope/irp.yaml</c>, for <c>hound irp build</c> and <c>hound scope validate</c>.</summary>
    public static (ScopeIrp? Irp, IReadOnlyList<ScopeProblem> Problems) ParseIrp(ScopeSource irp)
    {
        ArgumentNullException.ThrowIfNull(irp);
        var file = new FileReader(irp);
        if (file.Root() is not { } root)
        {
            return (null, file.Problems);
        }

        file.Keys(root, IrpKeys);
        var programme = file.Ref(root, "programme", required: true);
        var version = file.Required(root, "version");
        var roles = file.Map(root, "roles");
        if (roles is not null)
        {
            file.Keys(roles, [.. IrpLayout.Roles]);
        }

        var sections = file.Map(root, "sections");
        if (sections is not null)
        {
            file.Keys(sections, [.. IrpLayout.Sections]);
        }

        return programme is null || version is null
            ? (null, file.Problems)
            : (new ScopeIrp(
                programme,
                version,
                roles is null ? [] : [.. IrpLayout.Roles.Select(r => file.Ref(roles, r) is { } holder ? new SspRole(r, holder) : null).OfType<SspRole>()],
                sections is null
                    ? []
                    : [.. sections.Children.Where(c => c.Key is YamlScalarNode { Value: not null } k && IrpLayout.Sections.Contains(k.Value) && c.Value is YamlScalarNode { Value: not null })
                        .Select(c => new SspSection(((YamlScalarNode)c.Key).Value!, ((YamlScalarNode)c.Value).Value!, file.At(c.Value)))],
                file.At(root)), file.Problems);
    }
}
