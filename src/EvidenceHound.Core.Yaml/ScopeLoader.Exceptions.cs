using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads <c>scope/exceptions.yaml</c>: security exceptions requested through the repo (s15-03).</summary>
public static partial class ScopeLoader
{
    private static readonly string[] ExceptionKeys =
    [
        "ref_id", "name", "description", "domain", "severity", "owners", "expiration_date", "observation", "systems", "controls", "scenarios", "requirements",
    ];

    private static readonly string[] ExceptionRequirementKeys = ["audit", "requirement"];

    /// <summary>Only <c>scope/exceptions.yaml</c>, for <c>hound status</c>, which lists what expires without reading the whole scope.</summary>
    public static (IReadOnlyList<ScopeExceptionRequest> Exceptions, IReadOnlyList<ScopeProblem> Problems) ParseExceptions(ScopeSource exceptions)
    {
        ArgumentNullException.ThrowIfNull(exceptions);
        var file = new FileReader(exceptions);
        var parsed = file.Root() is { } root ? ReadExceptions(root, file) : [];
        return (parsed, file.Problems);
    }

    private static ScopeExceptionRequest[] ReadExceptions(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, ["exceptions"]);
        return [.. file.Items(root, "exceptions").Select(item => ReadException(item, file)).OfType<ScopeExceptionRequest>()];
    }

    private static ScopeExceptionRequest? ReadException(YamlMappingNode item, FileReader file)
    {
        if (FileReader.Child(item, "status") is { } status)
        {
            file.Problem(
                "scope.approval_not_configurable",
                file.At(status),
                "an exception's status is its approval, which the repo does not set.",
                "Remove 'status'. Apply the exception as a request, and its approver in CISO Assistant approves or rejects it there.");
            item = new YamlMappingNode(item.Children.Where(c => c.Key is not YamlScalarNode { Value: "status" }));
        }

        file.Keys(item, ExceptionKeys);
        var refId = file.Required(item, "ref_id");
        var name = file.Required(item, "name");
        var domain = file.Ref(item, "domain", required: true);
        var severity = file.OneOf(item, "severity", [.. ScopeExceptionRequest.Severities], required: false) ?? "undefined";
        var requirements = file.Items(item, "requirements").Select(r => ReadExceptionRequirement(r, file)).OfType<ExceptionRequirement>().ToArray();
        return refId is null || name is null || domain is null
            ? null
            : new ScopeExceptionRequest(
                refId, name, file.Text(item, "description"), domain, severity, file.Emails(item, "owners"), file.Text(item, "expiration_date"),
                file.Text(item, "observation"), file.Refs(item, "systems"), file.Refs(item, "controls"), file.Refs(item, "scenarios"), requirements, file.At(item));
    }

    private static ExceptionRequirement? ReadExceptionRequirement(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, ExceptionRequirementKeys);
        var audit = file.Required(item, "audit");
        var requirement = file.Required(item, "requirement");
        return audit is null || requirement is null ? null : new ExceptionRequirement(audit, requirement, file.At(item));
    }
}
