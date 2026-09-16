namespace Cerberus.Domain.Authorization;

public enum RoleScope
{
    /// <summary>Applies to the whole platform (never tied to an organization).</summary>
    Platform = 0,

    /// <summary>Applies to exactly one organization.</summary>
    Organization = 1
}

/// <summary>
/// Administrative permission of the identity provider itself. The catalogue is fixed in code
/// (<see cref="PermissionCatalog"/>) and mirrored in the database for referential integrity.
/// Business permissions of client applications are deliberately not managed here: each application owns its RBAC.
/// </summary>
public sealed class Permission
{
    public string Code { get; private set; } = string.Empty;
    public RoleScope Scope { get; private set; }
    public string Description { get; private set; } = string.Empty;

    private Permission()
    {
    }

    internal Permission(string code, RoleScope scope, string description)
    {
        Code = code;
        Scope = scope;
        Description = description;
    }
}

public static class PermissionCatalog
{
    public const string PlatformUsersManage = "platform.users.manage";
    public const string PlatformOrganizationsManage = "platform.organizations.manage";
    public const string PlatformClientsManage = "platform.clients.manage";
    public const string PlatformScopesManage = "platform.scopes.manage";
    public const string PlatformRolesManage = "platform.roles.manage";
    public const string PlatformSecurityManage = "platform.security.manage";
    public const string PlatformAuditRead = "platform.audit.read";

    public const string OrganizationMembersRead = "org.members.read";
    public const string OrganizationMembersManage = "org.members.manage";
    public const string OrganizationInvitationsManage = "org.invitations.manage";
    public const string OrganizationRolesManage = "org.roles.manage";
    public const string OrganizationApplicationsManage = "org.applications.manage";
    public const string OrganizationSettingsManage = "org.settings.manage";

    public static readonly IReadOnlyList<Permission> All =
    [
        new(PlatformUsersManage, RoleScope.Platform, "Manage global user accounts"),
        new(PlatformOrganizationsManage, RoleScope.Platform, "Manage organizations and their administrators"),
        new(PlatformClientsManage, RoleScope.Platform, "Manage client applications and OIDC clients"),
        new(PlatformScopesManage, RoleScope.Platform, "Manage API scopes"),
        new(PlatformRolesManage, RoleScope.Platform, "Manage platform roles and assignments"),
        new(PlatformSecurityManage, RoleScope.Platform, "Manage signing keys and global security settings"),
        new(PlatformAuditRead, RoleScope.Platform, "Read the global audit log"),
        new(OrganizationMembersRead, RoleScope.Organization, "View organization members"),
        new(OrganizationMembersManage, RoleScope.Organization, "Suspend, resume and remove members"),
        new(OrganizationInvitationsManage, RoleScope.Organization, "Invite users to the organization"),
        new(OrganizationRolesManage, RoleScope.Organization, "Manage organization roles and assignments"),
        new(OrganizationApplicationsManage, RoleScope.Organization, "Enable or disable applications for the organization"),
        new(OrganizationSettingsManage, RoleScope.Organization, "Manage organization settings")
    ];

    public static Permission? Find(string code) => All.FirstOrDefault(p => p.Code == code);

    public static IEnumerable<string> CodesFor(RoleScope scope) => All.Where(p => p.Scope == scope).Select(p => p.Code);
}
