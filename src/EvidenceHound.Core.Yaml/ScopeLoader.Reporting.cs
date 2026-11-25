using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads <c>scope/reporting.yaml</c>: the reports an incident owes, to whom and within how long (s15-07).</summary>
public static partial class ScopeLoader
{
    private static readonly string[] ObligationKeys = ["id", "name", "report_to", "within", "when", "reference"];
    private static readonly string[] ObligationTriggerKeys = ["data", "always"];

    /// <summary>Only <c>scope/reporting.yaml</c>, for <c>hound incident obligations</c> and <c>hound scope validate</c>.</summary>
    public static (IReadOnlyList<ReportingObligation> Obligations, IReadOnlyList<ScopeProblem> Problems) ParseReporting(ScopeSource reporting)
    {
        ArgumentNullException.ThrowIfNull(reporting);
        var file = new FileReader(reporting);
        if (file.Root() is not { } root)
        {
            return ([], file.Problems);
        }

        file.Keys(root, ["obligations"]);
        var obligations = file.Items(root, "obligations").Select(item => ReadObligation(item, file)).OfType<ReportingObligation>().ToList();
        foreach (var duplicate in obligations.GroupBy(o => o.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1)))
        {
            file.Problem("reporting.duplicate", duplicate.At, $"reporting obligation '{duplicate.Id}' is declared more than once.", "Give each obligation a different id.");
        }

        return (obligations, file.Problems);
    }

    private static ReportingObligation? ReadObligation(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, ObligationKeys);
        var id = file.Required(item, "id");
        var name = file.Required(item, "name");
        var reportTo = file.Required(item, "report_to");
        var within = ReadDuration(item, "within", file, required: true);
        var when = file.Map(item, "when");
        if (when is null)
        {
            file.Problem("scope.key_missing", file.At(item), "a reporting obligation needs 'when': the data kinds that trigger it, or 'always: true'.", "Add when: {data: [cui]} or when: {always: true}.");
        }
        else
        {
            file.Keys(when, ObligationTriggerKeys);
        }

        var data = new List<SensitiveDataKind>();
        foreach (var kind in when is null ? [] : file.Refs(when, "data"))
        {
            if (DataKinds.Contains(kind.Value, StringComparer.Ordinal))
            {
                data.Add(Kind(kind.Value));
            }
            else
            {
                file.Invalid(FileReader.Child(when!, "data")!, $"'data' names '{kind.Value}'; expected one of {string.Join(", ", DataKinds)}.");
            }
        }

        var always = when is not null && file.OneOf(when, "always", Booleans, required: false) == "true";
        return id is null || name is null || reportTo is null || within is null || when is null
            ? null
            : new ReportingObligation(id, name, reportTo, within.Value, data, always, file.At(item)) { Reference = file.Text(item, "reference") };
    }
}
