using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary><c>scope/capabilities.yaml</c>: which system provides each mitigation of one framework.</summary>
public static partial class ScopeLoader
{
    private static readonly string[] CapabilityMapKeys = ["framework", "mitigations"];

    private static readonly string[] CapabilityKeys =
        ["mitigation", "name", "provided_by", "systems", "supplier", "owner", "evidence", "route", "approved_by", "approved_on", "reason", "signals"];

    private static readonly string[] Provisions = ["system", "manual", "gap"];
    private static readonly string[] EvidenceRoutes = ["collector", "profile", "manual"];

    private static ScopeCapabilityMap? ReadCapabilities(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, CapabilityMapKeys);
        var framework = file.Ref(root, "framework", required: true);
        return framework is null
            ? null
            : new ScopeCapabilityMap(framework, [.. file.Items(root, "mitigations").Select(item => ReadCapability(item, file)).OfType<ScopeCapability>()], file.At(root));
    }

    private static ScopeCapability? ReadCapability(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, CapabilityKeys);
        var mitigation = file.Required(item, "mitigation");
        var provision = file.OneOf(item, "provided_by", Provisions, required: true);
        return mitigation is null || provision is null
            ? null
            : new ScopeCapability(mitigation, provision switch { "system" => CapabilityProvision.System, "manual" => CapabilityProvision.Manual, _ => CapabilityProvision.Gap }, file.At(item))
            {
                Name = file.Text(item, "name"),
                Systems = file.Refs(item, "systems"),
                Supplier = file.Ref(item, "supplier"),
                Owner = file.Text(item, "owner"),
                Evidence = file.OneOf(item, "evidence", EvidenceRoutes, required: false),
                Route = file.Text(item, "route"),
                ApprovedBy = file.Text(item, "approved_by"),
                ApprovedOn = file.Text(item, "approved_on"),
                Reason = file.Text(item, "reason"),
                Signals = [.. file.Refs(item, "signals").Select(s => s.Value)],
            };
    }
}
