using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Authorization;

/// <summary>
/// Role of the identity provider. Invariants:
/// - a platform role has no organization, an organization role has exactly one organization;
/// - a role only holds permissions of its own scope (a platform permission can never reach an organization role);
/// - the name is unique within its scope (platform or organization);
/// - system roles cannot be renamed, deleted or have their permissions changed.
/// </summary>
public sealed class Role : Entity
{
    public const int NameMaxLength = 100;

    public const string PlatformAdministratorName = "Platform administrator";
    public const string OrganizationAdministratorName = "Organization administrator";

    private readonly List<RolePermission> _permissions = [];

    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public RoleScope Scope { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    public IEnumerable<string> PermissionCodes => _permissions.Select(p => p.PermissionCode);

    private Role()
    {
    }

    private Role(DateTime utcNow) : base(utcNow)
    {
    }

    public static Role CreatePlatformRole(string name, string? description, bool isSystem, DateTime utcNow)
    {
        var role = new Role(utcNow) { Scope = RoleScope.Platform, IsSystem = isSystem };
        role.ApplyName(name, description);
        return role;
    }

    public static Role CreateOrganizationRole(Guid organizationId, string name, string? description, bool isSystem, DateTime utcNow)
    {
        var role = new Role(utcNow)
        {
            Scope = RoleScope.Organization,
            OrganizationId = Check.NotEmpty(organizationId, nameof(OrganizationId)),
            IsSystem = isSystem
        };
        role.ApplyName(name, description);
        return role;
    }

    public void Update(string name, string? description, DateTime utcNow)
    {
        EnsureNotSystem();
        ApplyName(name, description);
        Touch(utcNow);
    }

    public void SetPermissions(IEnumerable<string> permissionCodes, DateTime utcNow)
    {
        EnsureNotSystem();
        ReplacePermissions(permissionCodes);
        Touch(utcNow);
    }

    /// <summary>Used by seeding only, to (re)initialize system roles.</summary>
    public void InitializeSystemPermissions(IEnumerable<string> permissionCodes, DateTime utcNow)
    {
        if (!IsSystem)
        {
            throw new InvalidDomainOperationException("Only system roles can be initialized.");
        }

        ReplacePermissions(permissionCodes);
        Touch(utcNow);
    }

    public bool HasPermission(string code) => _permissions.Any(p => p.PermissionCode == code);

    public void EnsureDeletable()
    {
        EnsureNotSystem();
    }

    private void ReplacePermissions(IEnumerable<string> permissionCodes)
    {
        var codes = permissionCodes.Distinct(StringComparer.Ordinal).ToList();
        foreach (var code in codes)
        {
            var permission = PermissionCatalog.Find(code)
                ?? throw new DomainValidationException($"Unknown permission '{code}'.", nameof(Permissions));
            if (permission.Scope != Scope)
            {
                throw new DomainValidationException(
                    $"Permission '{code}' has scope {permission.Scope} and cannot be granted to a {Scope} role.", nameof(Permissions));
            }
        }

        _permissions.RemoveAll(p => !codes.Contains(p.PermissionCode));
        foreach (var code in codes.Where(c => _permissions.All(p => p.PermissionCode != c)))
        {
            _permissions.Add(new RolePermission(Id, code));
        }
    }

    private void ApplyName(string name, string? description)
    {
        Name = Check.Required(name, nameof(Name), NameMaxLength);
        NormalizedName = IdentityNormalizer.Normalize(Name);
        Description = Check.Optional(description, nameof(Description), 500);
    }

    private void EnsureNotSystem()
    {
        if (IsSystem)
        {
            throw new InvalidDomainOperationException("System roles cannot be modified or deleted.");
        }
    }
}

public sealed class RolePermission
{
    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;

    private RolePermission()
    {
    }

    internal RolePermission(Guid roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }
}
