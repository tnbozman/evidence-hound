using System.Globalization;
using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>The <c>data</c> list on a system or a supplier's solution (s10-06).</summary>
public static partial class ScopeLoader
{
    private static readonly string[] DataKeys = ["kind", "detail", "role", "authority"];
    private static readonly string[] SystemDataKeys = ["kind", "detail", "role"];
    private static readonly string[] AuthorityKeys = ["name", "evidence", "expires"];
    private static readonly string[] DataKinds = ["cui", "pii", "export_controlled", "classified"];
    private static readonly string[] DataRoles = ["stores", "processes", "transmits", "protects"];

    private static SensitiveData[] ReadData(YamlMappingNode owner, FileReader file, bool authority) =>
        [.. file.Items(owner, "data").Select(item => ReadDatum(item, file, authority)).OfType<SensitiveData>()];

    private static SensitiveData? ReadDatum(YamlMappingNode item, FileReader file, bool authority)
    {
        file.Keys(item, authority ? DataKeys : SystemDataKeys);
        var kind = file.OneOf(item, "kind", DataKinds, required: true);
        var role = file.OneOf(item, "role", DataRoles, required: true);
        var detail = file.Text(item, "detail");
        if (kind is "classified" or "export_controlled" && detail is null)
        {
            file.Problem(
                "data.detail_missing",
                file.At(item),
                $"'{kind}' data needs a 'detail' saying which.",
                kind == "classified" ? "Name the classification, such as OFFICIAL: Sensitive or PROTECTED." : "Name the regime, such as ITAR, EAR or DSGL.");
        }

        if (kind is null || role is null)
        {
            return null;
        }

        return new SensitiveData(Kind(kind), Enum.Parse<DataRole>(role, ignoreCase: true), file.At(item))
        {
            Detail = detail,
            Authority = authority && file.Map(item, "authority") is { } map ? ReadAuthority(map, file) : null,
        };
    }

    private static DataAuthority? ReadAuthority(YamlMappingNode map, FileReader file)
    {
        file.Keys(map, AuthorityKeys);
        var name = file.Required(map, "name");
        var expires = file.Text(map, "expires");
        DateOnly? date = null;
        if (expires is not null)
        {
            if (DateOnly.TryParseExact(expires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                date = parsed;
            }
            else
            {
                file.Invalid(FileReader.Child(map, "expires")!, $"'expires' is '{expires}'; expected a date as yyyy-MM-dd.");
            }
        }

        return name is null ? null : new DataAuthority(name, file.At(map)) { Evidence = file.Text(map, "evidence"), Expires = date };
    }

    private static SensitiveDataKind Kind(string code) => code switch
    {
        "export_controlled" => SensitiveDataKind.ExportControlled,
        _ => Enum.Parse<SensitiveDataKind>(code, ignoreCase: true),
    };
}
