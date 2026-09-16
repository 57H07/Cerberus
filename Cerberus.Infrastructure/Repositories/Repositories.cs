using Cerberus.Application.Common;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Domain.Applications;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Common;
using Cerberus.Domain.Consents;
using Cerberus.Domain.Keys;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Sessions;
using Cerberus.Domain.Users;
using Cerberus.Infrastructure.Collections;
using Cerberus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Cerberus.Infrastructure.Repositories;

public class UserRepository(CerberusDbContext context) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<User?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default)
        => context.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName, cancellationToken);

    public async Task<IReadOnlyList<User>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        return await context.Users.Where(u => list.Contains(u.Id)).ToListAsync(cancellationToken);
    }

    public Task<PagedResult<User>> GetPagedAsync(PagedFilter filter, CancellationToken cancellationToken = default)
    {
        var query = context.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = IdentityNormalizer.Normalize(filter.Search);
            query = query.Where(u => u.NormalizedEmail.Contains(search) || u.NormalizedUserName.Contains(search)
                || u.LastName.Contains(filter.Search) || u.FirstName.Contains(filter.Search));
        }

        return query.OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ToPagedResultAsync(filter, cancellationToken);
    }

    public void Add(User user) => context.Users.Add(user);
}

public class OrganizationRepository(CerberusDbContext context) : IOrganizationRepository
{
    public Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Organizations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Organization?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
        => context.Organizations.FirstOrDefaultAsync(o => o.Slug == slug, cancellationToken);

    public async Task<IReadOnlyList<Organization>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        return await context.Organizations.Where(o => list.Contains(o.Id)).OrderBy(o => o.Name).ToListAsync(cancellationToken);
    }

    public Task<PagedResult<Organization>> GetPagedAsync(PagedFilter filter, CancellationToken cancellationToken = default)
    {
        var query = context.Organizations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(o => o.Name.Contains(filter.Search) || o.Slug.Contains(filter.Search));
        }

        return query.OrderBy(o => o.Name).ToPagedResultAsync(filter, cancellationToken);
    }

    public void Add(Organization organization) => context.Organizations.Add(organization);
}

public class MembershipRepository(CerberusDbContext context) : IMembershipRepository
{
    public Task<OrganizationMembership?> GetAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
        => context.Memberships.FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<OrganizationMembership>> ListByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await context.Memberships.Where(m => m.OrganizationId == organizationId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrganizationMembership>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await context.Memberships.Where(m => m.UserId == userId).ToListAsync(cancellationToken);

    public void Add(OrganizationMembership membership) => context.Memberships.Add(membership);
}

public class InvitationRepository(CerberusDbContext context) : IInvitationRepository
{
    public Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => context.Invitations.FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

    public Task<Invitation?> GetAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken = default)
        => context.Invitations.FirstOrDefaultAsync(i => i.OrganizationId == organizationId && i.Id == invitationId, cancellationToken);

    public async Task<IReadOnlyList<Invitation>> ListByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await context.Invitations.Where(i => i.OrganizationId == organizationId).OrderByDescending(i => i.CreatedAt).ToListAsync(cancellationToken);

    public void Add(Invitation invitation) => context.Invitations.Add(invitation);
}

public class ClientApplicationRepository(CerberusDbContext context) : IClientApplicationRepository
{
    public Task<ClientApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.ClientApplications.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ClientApplication>> ListAsync(CancellationToken cancellationToken = default)
        => await context.ClientApplications.OrderBy(a => a.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ClientApplication>> ListGrantedToOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await context.ClientApplications
            .Where(a => a.OrganizationAccesses.Any(x => x.OrganizationId == organizationId))
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

    public void Add(ClientApplication application) => context.ClientApplications.Add(application);
}

public class OidcClientRepository(CerberusDbContext context) : IOidcClientRepository
{
    public Task<OidcClient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.OidcClients.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<OidcClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
        => context.OidcClients.FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

    public async Task<IReadOnlyList<OidcClient>> ListAsync(CancellationToken cancellationToken = default)
        => await context.OidcClients.OrderBy(c => c.ClientId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OidcClient>> ListByApplicationAsync(Guid clientApplicationId, CancellationToken cancellationToken = default)
        => await context.OidcClients.Where(c => c.ClientApplicationId == clientApplicationId).OrderBy(c => c.ClientId).ToListAsync(cancellationToken);

    public void Add(OidcClient client) => context.OidcClients.Add(client);
}

public class ApiScopeRepository(CerberusDbContext context) : IApiScopeRepository
{
    public Task<ApiScope?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.ApiScopes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ApiScope>> ListAsync(CancellationToken cancellationToken = default)
        => await context.ApiScopes.OrderBy(s => s.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ApiScope>> GetByNamesAsync(IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        var list = names.Distinct().ToList();
        return await context.ApiScopes.Where(s => list.Contains(s.Name)).ToListAsync(cancellationToken);
    }

    public void Add(ApiScope scope) => context.ApiScopes.Add(scope);
}

public class RoleRepository(CerberusDbContext context) : IRoleRepository
{
    public Task<Role?> GetPlatformRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
        => context.Roles.FirstOrDefaultAsync(r => r.Id == roleId && r.Scope == RoleScope.Platform, cancellationToken);

    public Task<Role?> GetOrganizationRoleAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken = default)
        => context.Roles.FirstOrDefaultAsync(
            r => r.Id == roleId && r.Scope == RoleScope.Organization && r.OrganizationId == organizationId, cancellationToken);

    public Task<Role?> FindPlatformRoleByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = IdentityNormalizer.Normalize(name);
        return context.Roles.FirstOrDefaultAsync(r => r.Scope == RoleScope.Platform && r.NormalizedName == normalized, cancellationToken);
    }

    public Task<Role?> FindOrganizationRoleByNameAsync(Guid organizationId, string name, CancellationToken cancellationToken = default)
    {
        var normalized = IdentityNormalizer.Normalize(name);
        return context.Roles.FirstOrDefaultAsync(
            r => r.Scope == RoleScope.Organization && r.OrganizationId == organizationId && r.NormalizedName == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<Role>> ListPlatformRolesAsync(CancellationToken cancellationToken = default)
        => await context.Roles.Where(r => r.Scope == RoleScope.Platform).OrderBy(r => r.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Role>> ListOrganizationRolesAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await context.Roles.Where(r => r.Scope == RoleScope.Organization && r.OrganizationId == organizationId)
            .OrderBy(r => r.Name).ToListAsync(cancellationToken);

    public void Add(Role role) => context.Roles.Add(role);

    public void Remove(Role role) => context.Roles.Remove(role);
}

public class RoleAssignmentRepository(CerberusDbContext context) : IRoleAssignmentRepository
{
    public Task<UserRoleAssignment?> GetPlatformAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default)
        => context.RoleAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.OrganizationId == null, cancellationToken);

    public Task<UserRoleAssignment?> GetOrganizationAssignmentAsync(Guid organizationId, Guid assignmentId, CancellationToken cancellationToken = default)
        => context.RoleAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.OrganizationId == organizationId, cancellationToken);

    public Task<bool> ExistsAsync(Guid userId, Guid roleId, Guid? organizationId, CancellationToken cancellationToken = default)
        => context.RoleAssignments.AnyAsync(a => a.UserId == userId && a.RoleId == roleId && a.OrganizationId == organizationId, cancellationToken);

    public Task<bool> AnyForRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
        => context.RoleAssignments.AnyAsync(a => a.RoleId == roleId, cancellationToken);

    public Task<int> CountUsersWithRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
        => context.RoleAssignments
            .Where(a => a.RoleId == roleId)
            .Join(context.Users.Where(u => u.Status == UserStatus.Active), a => a.UserId, u => u.Id, (a, _) => a.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

    public async Task<IReadOnlyList<RoleAssignmentView>> ListPlatformAssignmentsAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        var query = from a in context.RoleAssignments
                    join r in context.Roles on a.RoleId equals r.Id
                    where a.OrganizationId == null && r.Scope == RoleScope.Platform
                    select new { a, r };
        if (userId is not null)
        {
            query = query.Where(x => x.a.UserId == userId);
        }

        return await query
            .OrderBy(x => x.r.Name)
            .Select(x => new RoleAssignmentView(x.a.Id, x.a.UserId, x.r.Id, x.r.Name, x.r.Scope, null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleAssignmentView>> ListOrganizationAssignmentsAsync(Guid organizationId, Guid? userId, CancellationToken cancellationToken = default)
    {
        var query = from a in context.RoleAssignments
                    join r in context.Roles on a.RoleId equals r.Id
                    where a.OrganizationId == organizationId && r.OrganizationId == organizationId
                    select new { a, r };
        if (userId is not null)
        {
            query = query.Where(x => x.a.UserId == userId);
        }

        return await query
            .OrderBy(x => x.r.Name)
            .Select(x => new RoleAssignmentView(x.a.Id, x.a.UserId, x.r.Id, x.r.Name, x.r.Scope, x.a.OrganizationId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetPlatformPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var codes = await (from a in context.RoleAssignments
                           join r in context.Roles on a.RoleId equals r.Id
                           join p in context.RolePermissions on r.Id equals p.RoleId
                           where a.UserId == userId && a.OrganizationId == null && r.Scope == RoleScope.Platform
                           select p.PermissionCode).Distinct().ToListAsync(cancellationToken);
        return codes.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlySet<string>> GetOrganizationPermissionsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        // Permissions only count while the membership is active: suspending a member immediately removes its rights.
        var codes = await (from a in context.RoleAssignments
                           join r in context.Roles on a.RoleId equals r.Id
                           join p in context.RolePermissions on r.Id equals p.RoleId
                           join m in context.Memberships on new { a.UserId, OrganizationId = a.OrganizationId!.Value } equals new { m.UserId, m.OrganizationId }
                           where a.UserId == userId
                               && a.OrganizationId == organizationId
                               && r.OrganizationId == organizationId
                               && m.Status == MembershipStatus.Active
                           select p.PermissionCode).Distinct().ToListAsync(cancellationToken);
        return codes.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<Guid>> ListOrganizationIdsWithAnyPermissionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await (from a in context.RoleAssignments
                      join r in context.Roles on a.RoleId equals r.Id
                      join m in context.Memberships on new { a.UserId, OrganizationId = a.OrganizationId!.Value } equals new { m.UserId, m.OrganizationId }
                      where a.UserId == userId && a.OrganizationId != null && m.Status == MembershipStatus.Active && r.Permissions.Any()
                      select a.OrganizationId!.Value).Distinct().ToListAsync(cancellationToken);
    }

    public async Task RemoveOrganizationAssignmentsForUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var assignments = await context.RoleAssignments
            .Where(a => a.OrganizationId == organizationId && a.UserId == userId)
            .ToListAsync(cancellationToken);
        context.RoleAssignments.RemoveRange(assignments);
    }

    public void Add(UserRoleAssignment assignment) => context.RoleAssignments.Add(assignment);

    public void Remove(UserRoleAssignment assignment) => context.RoleAssignments.Remove(assignment);
}

public class UserConsentRepository(CerberusDbContext context) : IUserConsentRepository
{
    public Task<UserConsent?> GetAsync(Guid userId, Guid oidcClientId, Guid organizationId, CancellationToken cancellationToken = default)
        => context.Consents.FirstOrDefaultAsync(
            c => c.UserId == userId && c.OidcClientId == oidcClientId && c.OrganizationId == organizationId, cancellationToken);

    public Task<UserConsent?> GetForUserAsync(Guid userId, Guid consentId, CancellationToken cancellationToken = default)
        => context.Consents.FirstOrDefaultAsync(c => c.UserId == userId && c.Id == consentId, cancellationToken);

    public async Task<IReadOnlyList<UserConsent>> ListActiveForUserAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken = default)
        => await context.Consents
            .Where(c => c.UserId == userId && c.RevokedAt == null && (c.ExpiresAt == null || c.ExpiresAt > utcNow))
            .OrderByDescending(c => c.GrantedAt)
            .ToListAsync(cancellationToken);

    public void Add(UserConsent consent) => context.Consents.Add(consent);
}

public class UserSessionRepository(CerberusDbContext context) : IUserSessionRepository
{
    public Task<UserSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => context.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

    public async Task<IReadOnlyList<UserSession>> ListActiveForUserAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken = default)
        => await context.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > utcNow)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(cancellationToken);

    public void Add(UserSession session) => context.Sessions.Add(session);
}

public class AuditRepository(CerberusDbContext context) : IAuditRepository
{
    public Task<PagedResult<AuditEntry>> GetPagedAsync(AuditFilter filter, CancellationToken cancellationToken = default)
    {
        var query = context.AuditEntries.AsNoTracking();
        if (filter.OrganizationId is not null)
        {
            query = query.Where(a => a.OrganizationId == filter.OrganizationId);
        }

        if (filter.ActorUserId is not null)
        {
            query = query.Where(a => a.ActorUserId == filter.ActorUserId);
        }

        if (!string.IsNullOrWhiteSpace(filter.ActionPrefix))
        {
            query = query.Where(a => a.Action.StartsWith(filter.ActionPrefix));
        }

        return query.OrderByDescending(a => a.OccurredAt).ToPagedResultAsync(filter, cancellationToken);
    }

    public void Add(AuditEntry entry) => context.AuditEntries.Add(entry);
}

public class SigningKeyRepository(CerberusDbContext context) : ISigningKeyRepository
{
    public async Task<IReadOnlyList<SigningKey>> ListAsync(KeyUsage usage, CancellationToken cancellationToken = default)
        => await context.SigningKeys.Where(k => k.Usage == usage).OrderByDescending(k => k.ActivatesAt).ToListAsync(cancellationToken);

    public void Add(SigningKey key) => context.SigningKeys.Add(key);
}
