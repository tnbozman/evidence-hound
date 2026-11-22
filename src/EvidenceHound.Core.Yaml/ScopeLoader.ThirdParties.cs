using System.Globalization;
using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary><c>scope/third-parties/&lt;entity&gt;.yaml</c>: one supplier or other third party per file.</summary>
public static partial class ScopeLoader
{
    private static readonly string[] EntityKeys =
    [
        "ref_id", "name", "description", "mission", "country", "currency", "parent", "domain", "dependency", "penetration", "maturity", "trust",
        "identifiers", "solutions", "contracts", "representatives",
    ];

    private static readonly string[] OrganisationEntityKeys = ["name", "description", "mission", "country", "currency", "identifiers"];

    private static readonly string[] SolutionKeys = ["ref_id", "name", "description", "criticality"];
    private static readonly string[] ContractKeys = ["ref_id", "name", "description", "status", "start", "end", "annual_expense", "currency", "solutions"];
    private static readonly string[] ContractStatuses = ["draft", "active", "expired", "terminated"];
    private static readonly string[] RepresentativeKeys = ["email", "first_name", "last_name", "role", "phone"];
    private static readonly string[] Levels = ["dependency", "penetration", "maturity", "trust"];
    private static readonly string[] Codes = ["country", "currency"];

    private static ScopeEntity? ReadEntity(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, EntityKeys);
        file.Secrets(root, SecretKeys);
        var refId = file.Required(root, "ref_id");
        var name = file.Required(root, "name");
        if (refId is null || name is null)
        {
            return null;
        }

        var levels = Levels.Select(l => (Name: l, Value: Whole(root, l, file))).ToArray();
        return new ScopeEntity(refId, name, file.At(root))
        {
            Description = file.Text(root, "description"),
            Mission = file.Text(root, "mission"),
            Country = file.Text(root, "country"),
            Currency = file.Text(root, "currency"),
            Parent = file.Ref(root, "parent"),
            Domain = file.Ref(root, "domain"),
            Dependency = levels[0].Value,
            Penetration = levels[1].Value,
            Maturity = levels[2].Value,
            Trust = levels[3].Value,
            Identifiers = file.Map(root, "identifiers") is { } identifiers ? file.TextMap(identifiers) : new Dictionary<string, string>(),
            Solutions = [.. file.Items(root, "solutions").Select(item => ReadSolution(item, file)).OfType<ScopeSolution>()],
            Contracts = [.. file.Items(root, "contracts").Select(item => ReadContract(item, file)).OfType<ScopeContract>()],
            Representatives = [.. file.Items(root, "representatives").Select(item => ReadRepresentative(item, file)).OfType<ScopeRepresentative>()],
            LevelsAt = levels.Where(l => l.Value is not null).ToDictionary(l => l.Name, l => file.At(FileReader.Child(root, l.Name)!), StringComparer.Ordinal),
            CodesAt = Codes.Where(k => FileReader.Child(root, k) is not null)
                .ToDictionary(k => k, k => file.At(FileReader.Child(root, k)!), StringComparer.Ordinal),
        };
    }

    /// <summary><c>organisation.entity</c> in <c>scope/setup.yaml</c>: the organisation's own entity, which CISO Assistant creates itself.</summary>
    private static ScopeEntity? ReadOrganisation(YamlMappingNode entity, FileReader file)
    {
        file.Keys(entity, OrganisationEntityKeys);
        var name = file.Required(entity, "name");
        return name is null
            ? null
            : new ScopeEntity(ScopeDesired.OrganisationKey, name, file.At(entity))
            {
                Description = file.Text(entity, "description"),
                Mission = file.Text(entity, "mission"),
                Country = file.Text(entity, "country"),
                Currency = file.Text(entity, "currency"),
                Identifiers = file.Map(entity, "identifiers") is { } identifiers ? file.TextMap(identifiers) : new Dictionary<string, string>(),
                CodesAt = Codes.Where(k => FileReader.Child(entity, k) is not null)
                    .ToDictionary(k => k, k => file.At(FileReader.Child(entity, k)!), StringComparer.Ordinal),
            };
    }

    private static ScopeSolution? ReadSolution(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, SolutionKeys);
        var refId = file.Required(item, "ref_id");
        var name = file.Required(item, "name");
        return refId is null || name is null ? null : new ScopeSolution(refId, name, file.Text(item, "description"), Whole(item, "criticality", file), file.At(item));
    }

    private static ScopeContract? ReadContract(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, ContractKeys);
        var refId = file.Required(item, "ref_id");
        var name = file.Required(item, "name");
        var status = file.OneOf(item, "status", ContractStatuses, required: true);
        var expense = file.Text(item, "annual_expense");
        if (expense is not null && !decimal.TryParse(expense, NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _))
        {
            expense = file.Invalid(FileReader.Child(item, "annual_expense")!, $"'annual_expense' is '{expense}'; expected a plain number, such as 12000.");
        }

        return refId is null || name is null || status is null
            ? null
            : new ScopeContract(refId, name, status, file.At(item))
            {
                Description = file.Text(item, "description"),
                Start = Date(item, "start", file),
                End = Date(item, "end", file),
                EndAt = FileReader.Child(item, "end") is { } end ? file.At(end) : null,
                AnnualExpense = expense,
                Currency = file.Text(item, "currency"),
                CurrencyAt = FileReader.Child(item, "currency") is { } currency ? file.At(currency) : null,
                Solutions = file.Refs(item, "solutions"),
            };
    }

    private static ScopeRepresentative? ReadRepresentative(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, [.. RepresentativeKeys, .. SecretKeys]);
        file.Secrets(item, SecretKeys);
        var email = file.Email(item, "email", required: true);
        return email is null
            ? null
            : new ScopeRepresentative(email.Value, email.At)
            {
                FirstName = file.Text(item, "first_name"),
                LastName = file.Text(item, "last_name"),
                Role = file.Text(item, "role"),
                Phone = file.Text(item, "phone"),
            };
    }

    /// <summary>A whole number, or null when left out; anything else is reported.</summary>
    private static int? Whole(YamlMappingNode map, string key, FileReader file)
    {
        var text = file.Text(map, key);
        if (text is null)
        {
            return null;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        file.Invalid(FileReader.Child(map, key)!, $"'{key}' is '{text}'; expected a whole number.");
        return null;
    }

    /// <summary>A date as yyyy-MM-dd, or null when left out; anything else is reported.</summary>
    private static string? Date(YamlMappingNode map, string key, FileReader file)
    {
        var text = file.Text(map, key);
        return text is null || DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? text
            : file.Invalid(FileReader.Child(map, key)!, $"'{key}' is '{text}'; expected a date as yyyy-MM-dd.");
    }
}
