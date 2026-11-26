using EvidenceHound.Core.Scope;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Core.Yaml;

/// <summary>Reads <c>access.yaml</c>: custom roles, users, teams and role grants.</summary>
public static partial class ScopeLoader
{
    private static readonly string[] AccessKeys = ["custom_roles", "users", "teams", "roles"];

    private static readonly string[] UserKeys = ["email", "first_name", "last_name", "active", "service_account"];

    private static readonly string[] TeamKeys = ["name", "description", "domain", "leader", "members", "email"];

    private static readonly string[] RoleKeys = ["role", "domain", "users", "teams"];

    private static readonly string[] ServiceAccounts = ["true", "publish", "apply"];

    private static ScopeAccess ReadAccess(YamlMappingNode root, FileReader file)
    {
        file.Keys(root, AccessKeys);
        var users = new List<ScopeUser>();
        foreach (var user in file.Items(root, "users").Select(item => ReadUser(item, file)).OfType<ScopeUser>())
        {
            if (users.Find(u => u.Email == user.Email) is { } first)
            {
                file.Problem(
                    "access.email_duplicate",
                    user.At,
                    $"'{user.Email}' is already listed at line {first.At.Line} (emails are compared without case).",
                    "List each person once.");
                continue;
            }

            users.Add(user);
        }

        return new ScopeAccess(
            users,
            [.. file.Items(root, "teams").Select(item => ReadTeam(item, file)).OfType<ScopeTeam>()],
            [.. file.Items(root, "roles").Select(item => ReadRoleGrant(item, file)).OfType<ScopeRoleGrant>()])
        {
            CustomRoles = file.Refs(root, "custom_roles"),
        };
    }

    private static ScopeUser? ReadUser(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, [.. UserKeys, .. SecretKeys]);
        file.Secrets(item, SecretKeys);
        var email = file.Email(item, "email", required: true);
        var active = file.OneOf(item, "active", Booleans, required: false);
        return email is null
            ? null
            : new ScopeUser(email.Value, file.Text(item, "first_name"), file.Text(item, "last_name"), active != "false", file.OneOf(item, "service_account", ServiceAccounts, required: false), email.At);
    }

    private static ScopeTeam? ReadTeam(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, [.. TeamKeys, .. SecretKeys]);
        file.Secrets(item, SecretKeys);
        var name = file.Required(item, "name");
        return name is null
            ? null
            : new ScopeTeam(name, file.Text(item, "description"), file.Ref(item, "domain"), file.Email(item, "leader"), file.Emails(item, "members"), file.At(item))
            {
                Email = file.Email(item, "email")?.Value,
            };
    }

    private static ScopeRoleGrant? ReadRoleGrant(YamlMappingNode item, FileReader file)
    {
        file.Keys(item, RoleKeys);
        var role = file.Ref(item, "role", required: true);
        var domain = file.Ref(item, "domain", required: true);
        return role is null || domain is null ? null : new ScopeRoleGrant(role, domain, file.Emails(item, "users"), file.Refs(item, "teams"), file.At(item));
    }
}
