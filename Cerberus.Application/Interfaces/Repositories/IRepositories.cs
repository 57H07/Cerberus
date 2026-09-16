using Cerberus.Application.Common;
using Cerberus.Domain.Applications;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Consents;
using Cerberus.Domain.Keys;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Sessions;
using Cerberus.Domain.Users;

namespace Cerberus.Application.Interfaces.Repositories;

// Repositories never save: IUnitOfWork.SaveChangesAsync commits the whole use case.
// Every organization-scoped read takes the organization id as an explicit, mandatory filter.

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<User?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<PagedResult<User>> GetPagedAsync(PagedFilter filter, CancellationToken cancellationToken = default);
    void Add(User user);
}

public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Organization?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Organization>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<PagedResult<Organization>> GetPagedAsync(PagedFilter filter, CancellationToken cancellationToken = default);
    void Add(Organization organization);
}

public interface IMembershipRepository
{
    Task<OrganizationMembership?> GetAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationMembership>> ListByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationMembership>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    void Add(OrganizationMembership membership);
}

public interface IInvitationRepository
{
    Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<Invitation?> GetAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Invitation>> ListByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    void Add(Invitation invitation);
}

public interface IClientApplicationRepository
{
    Task<ClientApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClientApplication>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClientApplication>> ListGrantedToOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    void Add(ClientApplication application);
}

public interface IOidcClientRepository
{
    Task<OidcClient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OidcClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OidcClient>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OidcClient>> ListByApplicationAsync(Guid clientApplicationId, CancellationToken cancellationToken = default);
    void Add(OidcClient client);
}

public interface IApiScopeRepository
{
    Task<ApiScope?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApiScope>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApiScope>> GetByNamesAsync(IEnumerable<string> names, CancellationToken cancellationToken = default);
    void Add(ApiScope scope);
}

public interface IRoleRepository
{
    Task<Role?> GetPlatformRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
    Task<Role?> GetOrganizationRoleAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken = default);
    Task<Role?> FindPlatformRoleByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<Role?> FindOrganizationRoleByNameAsync(Guid organizationId, string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Role>> ListPlatformRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Role>> ListOrganizationRolesAsync(Guid organizationId, CancellationToken cancellationToken = default);
    void Add(Role role);
    void Remove(Role role);
}

public sealed record RoleAssignmentView(Guid AssignmentId, Guid UserId, Guid RoleId, string RoleName, RoleScope Scope, Guid? OrganizationId);

public interface IRoleAssignmentRepository
{
    Task<UserRoleAssignment?> GetPlatformAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<UserRoleAssignment?> GetOrganizationAssignmentAsync(Guid organizationId, Guid assignmentId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid userId, Guid roleId, Guid? organizationId, CancellationToken cancellationToken = default);
    Task<bool> AnyForRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
    Task<int> CountUsersWithRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RoleAssignmentView>> ListPlatformAssignmentsAsync(Guid? userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RoleAssignmentView>> ListOrganizationAssignmentsAsync(Guid organizationId, Guid? userId, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> GetPlatformPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> GetOrganizationPermissionsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListOrganizationIdsWithAnyPermissionAsync(Guid userId, CancellationToken cancellationToken = default);
    Task RemoveOrganizationAssignmentsForUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    void Add(UserRoleAssignment assignment);
    void Remove(UserRoleAssignment assignment);
}

public interface IUserConsentRepository
{
    Task<UserConsent?> GetAsync(Guid userId, Guid oidcClientId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<UserConsent?> GetForUserAsync(Guid userId, Guid consentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserConsent>> ListActiveForUserAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken = default);
    void Add(UserConsent consent);
}

public interface IUserSessionRepository
{
    Task<UserSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSession>> ListActiveForUserAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken = default);
    void Add(UserSession session);
}

public interface IAuditRepository
{
    Task<PagedResult<AuditEntry>> GetPagedAsync(AuditFilter filter, CancellationToken cancellationToken = default);
    void Add(AuditEntry entry);
}

public interface ISigningKeyRepository
{
    Task<IReadOnlyList<SigningKey>> ListAsync(KeyUsage usage, CancellationToken cancellationToken = default);
    void Add(SigningKey key);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
