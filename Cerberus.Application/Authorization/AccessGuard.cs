using Cerberus.Application.Exceptions;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Application.Services;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;

namespace Cerberus.Application.Authorization;

/// <summary>
/// Single entry point for administrative authorization checks. Every administrative use case calls it first;
/// organization checks always require an active membership plus a role granting the permission in that organization.
/// Platform permissions never imply organization permissions (no implicit cross-tenant access).
/// </summary>
public interface IAccessGuard
{
    Guid RequireAuthenticatedUser();

    Task<IReadOnlySet<string>> GetPlatformPermissionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetOrganizationPermissionsAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<Guid> RequirePlatformPermissionAsync(string permission, CancellationToken cancellationToken = default);

    Task<Guid> RequireOrganizationPermissionAsync(Guid organizationId, string permission, CancellationToken cancellationToken = default);
}

public class AccessGuard(
    IActorContext actor,
    IRoleAssignmentRepository roleAssignmentRepository,
    IAuditWriter audit,
    IUnitOfWork unitOfWork) : IAccessGuard
{
    public Guid RequireAuthenticatedUser()
        => actor.UserId ?? throw new ForbiddenAccessException("Authentication is required.");

    public Task<IReadOnlySet<string>> GetPlatformPermissionsAsync(CancellationToken cancellationToken = default)
        => actor.UserId is null
            ? Task.FromResult<IReadOnlySet<string>>(new HashSet<string>())
            : roleAssignmentRepository.GetPlatformPermissionsAsync(actor.UserId.Value, cancellationToken);

    public Task<IReadOnlySet<string>> GetOrganizationPermissionsAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => actor.UserId is null
            ? Task.FromResult<IReadOnlySet<string>>(new HashSet<string>())
            : roleAssignmentRepository.GetOrganizationPermissionsAsync(actor.UserId.Value, organizationId, cancellationToken);

    public async Task<Guid> RequirePlatformPermissionAsync(string permission, CancellationToken cancellationToken = default)
    {
        EnsureScope(permission, RoleScope.Platform);
        var userId = RequireAuthenticatedUser();
        var permissions = await roleAssignmentRepository.GetPlatformPermissionsAsync(userId, cancellationToken);
        if (!permissions.Contains(permission))
        {
            await DenyAsync(null, permission, cancellationToken);
        }

        return userId;
    }

    public async Task<Guid> RequireOrganizationPermissionAsync(Guid organizationId, string permission, CancellationToken cancellationToken = default)
    {
        EnsureScope(permission, RoleScope.Organization);
        var userId = RequireAuthenticatedUser();
        var permissions = await roleAssignmentRepository.GetOrganizationPermissionsAsync(userId, organizationId, cancellationToken);
        if (!permissions.Contains(permission))
        {
            await DenyAsync(organizationId, permission, cancellationToken);
        }

        return userId;
    }

    private async Task DenyAsync(Guid? organizationId, string permission, CancellationToken cancellationToken)
    {
        audit.Write(AuditActions.CrossTenantAccessDenied, AuditOutcome.Denied, organizationId, details: new { permission });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        throw new ForbiddenAccessException("You are not allowed to perform this operation.");
    }

    private static void EnsureScope(string permission, RoleScope expected)
    {
        var definition = PermissionCatalog.Find(permission);
        if (definition is null || definition.Scope != expected)
        {
            throw new ArgumentException($"'{permission}' is not a {expected} permission.", nameof(permission));
        }
    }
}
