using System.Text.RegularExpressions;

namespace Cerberus.Domain.Clients;

public static partial class Scopes
{
    public const string OpenId = "openid";
    public const string Profile = "profile";
    public const string Email = "email";
    public const string OfflineAccess = "offline_access";

    /// <summary>Releases the user's roles in the selected organization (claim <c>org_roles</c>).</summary>
    public const string OrganizationRoles = "org.roles";

    public static readonly IReadOnlySet<string> Standard =
        new HashSet<string>(StringComparer.Ordinal) { OpenId, Profile, Email, OfflineAccess, OrganizationRoles };

    public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);

    [GeneratedRegex(@"^[a-z][a-z0-9._:-]{1,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
