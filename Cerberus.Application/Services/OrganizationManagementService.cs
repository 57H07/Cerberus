using System.Net;
using Cerberus.Application.Authorization;
using Cerberus.Application.Common;
using Cerberus.Application.DTOs;
using Cerberus.Application.Exceptions;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Security;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Domain.Applications;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;
using Cerberus.Domain.Common;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;
using Microsoft.Extensions.Options;

namespace Cerberus.Application.Services;

/// <summary>
/// Organization-level administration. Every method receives the organization id from the route and calls
/// <see cref="IAccessGuard.RequireOrganizationPermissionAsync"/> before reading anything; every repository call is
/// filtered by that organization id, so identifiers belonging to another organization resolve to "not found".
/// </summary>
public interface IOrganizationManagementService
{
    Task<AdministrationAccessDto> GetAdministrationAccessAsync(CancellationToken cancellationToken = default);
    Task<OrganizationDto> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task RenameAsync(Guid organizationId, string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task SuspendMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    Task ResumeMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvitationDto>> ListInvitationsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task InviteAsync(Guid organizationId, string email, CancellationToken cancellationToken = default);
    Task RevokeInvitationAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDto>> ListRolesAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<RoleDto> GetRoleAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken = default);
    Task<Guid> CreateRoleAsync(Guid organizationId, SaveRoleDto dto, CancellationToken cancellationToken = default);
    Task UpdateRoleAsync(Guid organizationId, Guid roleId, SaveRoleDto dto, CancellationToken cancellationToken = default);
    Task DeleteRoleAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken = default);
    Task AssignRoleAsync(Guid organizationId, Guid userId, Guid roleId, CancellationToken cancellationToken = default);
    Task UnassignRoleAsync(Guid organizationId, Guid assignmentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationApplicationDto>> ListApplicationsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task SetApplicationEnabledAsync(Guid organizationId, Guid applicationId, bool enabled, CancellationToken cancellationToken = default);

    Task<PagedResult<AuditEntryDto>> GetAuditAsync(Guid organizationId, AuditFilter filter, CancellationToken cancellationToken = default);
}

public class OrganizationManagementService(
    IAccessGuard guard,
    IOrganizationRepository organizationRepository,
    IMembershipRepository membershipRepository,
    IInvitationRepository invitationRepository,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IRoleAssignmentRepository assignmentRepository,
    IClientApplicationRepository applicationRepository,
    IAuditRepository auditRepository,
    ITokenRevocationService tokenRevocation,
    IEmailSender emailSender,
    ILinkBuilder linkBuilder,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<InvitationOptions> invitationOptions) : IOrganizationManagementService
{
    public async Task<AdministrationAccessDto> GetAdministrationAccessAsync(CancellationToken cancellationToken = default)
    {
        var userId = guard.RequireAuthenticatedUser();
        var platform = await guard.GetPlatformPermissionsAsync(cancellationToken);
        var organizationIds = await assignmentRepository.ListOrganizationIdsWithAnyPermissionAsync(userId, cancellationToken);
        var organizations = await organizationRepository.GetByIdsAsync(organizationIds, cancellationToken);
        return new AdministrationAccessDto(platform, organizations.Where(o => o.IsActive).Select(ToDto).ToList());
    }

    public async Task<OrganizationDto> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationMembersRead, cancellationToken);
        return ToDto(await GetActiveOrganizationAsync(organizationId, cancellationToken));
    }

    public async Task RenameAsync(Guid organizationId, string name, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationSettingsManage, cancellationToken);
        var organization = await GetActiveOrganizationAsync(organizationId, cancellationToken);
        organization.Rename(name, clock.UtcNow);
        audit.Write(AuditActions.OrganizationUpdated, organizationId: organizationId, targetType: nameof(Organization), targetId: organizationId,
            details: new { name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationMembersRead, cancellationToken);
        await GetActiveOrganizationAsync(organizationId, cancellationToken);

        var memberships = await membershipRepository.ListByOrganizationAsync(organizationId, cancellationToken);
        var users = await userRepository.GetByIdsAsync(memberships.Select(m => m.UserId), cancellationToken);
        var assignments = await assignmentRepository.ListOrganizationAssignmentsAsync(organizationId, null, cancellationToken);

        return memberships
            .Where(m => m.Status != MembershipStatus.Revoked)
            .Join(users, m => m.UserId, u => u.Id, (m, u) => new MemberDto(
                u.Id, u.UserName, u.Email, $"{u.FirstName} {u.LastName}", m.Status, u.Status,
                assignments.Where(a => a.UserId == u.Id).Select(a => new RoleAssignmentDto(a.AssignmentId, a.RoleId, a.RoleName)).ToList()))
            .OrderBy(m => m.FullName)
            .ToList();
    }

    public async Task SuspendMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var membership = await GetManageableMembershipAsync(organizationId, userId, cancellationToken);
        membership.Suspend(clock.UtcNow);
        audit.Write(AuditActions.MemberSuspended, organizationId: organizationId, targetType: nameof(User), targetId: userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await tokenRevocation.RevokeUserInOrganizationAsync(userId, organizationId, cancellationToken);
    }

    public async Task ResumeMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var membership = await GetManageableMembershipAsync(organizationId, userId, cancellationToken);
        membership.Resume(clock.UtcNow);
        audit.Write(AuditActions.MemberResumed, organizationId: organizationId, targetType: nameof(User), targetId: userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var membership = await GetManageableMembershipAsync(organizationId, userId, cancellationToken);
        membership.Revoke(clock.UtcNow);
        await assignmentRepository.RemoveOrganizationAssignmentsForUserAsync(organizationId, userId, cancellationToken);
        audit.Write(AuditActions.MemberRemoved, organizationId: organizationId, targetType: nameof(User), targetId: userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await tokenRevocation.RevokeUserInOrganizationAsync(userId, organizationId, cancellationToken);
    }

    public async Task<IReadOnlyList<InvitationDto>> ListInvitationsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationInvitationsManage, cancellationToken);
        var invitations = await invitationRepository.ListByOrganizationAsync(organizationId, cancellationToken);
        return invitations.Select(i => new InvitationDto(i.Id, i.Email, i.CreatedAt, i.ExpiresAt, i.AcceptedAt, i.RevokedAt)).ToList();
    }

    public async Task InviteAsync(Guid organizationId, string email, CancellationToken cancellationToken = default)
    {
        var actorId = await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationInvitationsManage, cancellationToken);
        var organization = await GetActiveOrganizationAsync(organizationId, cancellationToken);

        if (!User.IsValidEmail(email))
        {
            throw new Domain.Exceptions.DomainValidationException("Email address is invalid.", "Email");
        }

        var existingUser = await userRepository.FindByNormalizedEmailAsync(IdentityNormalizer.Normalize(email), cancellationToken);
        if (existingUser is not null)
        {
            var membership = await membershipRepository.GetAsync(organizationId, existingUser.Id, cancellationToken);
            if (membership is { Status: MembershipStatus.Active or MembershipStatus.Suspended })
            {
                throw new DuplicateEntityException("member", "email address");
            }
        }

        var token = SecureTokens.Generate();
        var invitation = Invitation.Create(organizationId, email, SecureTokens.Hash(token), actorId, invitationOptions.Value.Lifetime, clock.UtcNow);
        invitationRepository.Add(invitation);
        audit.Write(AuditActions.InvitationCreated, organizationId: organizationId, targetType: nameof(Invitation), targetId: invitation.Id,
            details: new { email = invitation.Email });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var link = linkBuilder.Invitation(token);
        await emailSender.SendAsync(
            invitation.Email,
            $"Invitation to join {organization.Name}",
            $"<p>You have been invited to join <strong>{WebUtility.HtmlEncode(organization.Name)}</strong>.</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Accept the invitation</a></p>",
            cancellationToken);
    }

    public async Task RevokeInvitationAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationInvitationsManage, cancellationToken);
        var invitation = await invitationRepository.GetAsync(organizationId, invitationId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Invitation), invitationId);
        invitation.Revoke(clock.UtcNow);
        audit.Write(AuditActions.InvitationRevoked, organizationId: organizationId, targetType: nameof(Invitation), targetId: invitationId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationMembersRead, cancellationToken);
        var roles = await roleRepository.ListOrganizationRolesAsync(organizationId, cancellationToken);
        return roles.Select(ToDto).ToList();
    }

    public async Task<RoleDto> GetRoleAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationRolesManage, cancellationToken);
        return ToDto(await GetRoleEntityAsync(organizationId, roleId, cancellationToken));
    }

    public async Task<Guid> CreateRoleAsync(Guid organizationId, SaveRoleDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationRolesManage, cancellationToken);
        await GetActiveOrganizationAsync(organizationId, cancellationToken);
        await EnsureCanGrantAsync(organizationId, dto.Permissions, cancellationToken);

        var role = Role.CreateOrganizationRole(organizationId, dto.Name, dto.Description, false, clock.UtcNow);
        role.SetPermissions(dto.Permissions, clock.UtcNow);
        roleRepository.Add(role);
        audit.Write(AuditActions.RoleCreated, organizationId: organizationId, targetType: nameof(Role), targetId: role.Id,
            details: new { role.Name, permissions = dto.Permissions });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return role.Id;
    }

    public async Task UpdateRoleAsync(Guid organizationId, Guid roleId, SaveRoleDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationRolesManage, cancellationToken);
        var role = await GetRoleEntityAsync(organizationId, roleId, cancellationToken);
        await EnsureCanGrantAsync(organizationId, role.PermissionCodes.Concat(dto.Permissions), cancellationToken);

        role.Update(dto.Name, dto.Description, clock.UtcNow);
        role.SetPermissions(dto.Permissions, clock.UtcNow);
        audit.Write(AuditActions.RoleUpdated, organizationId: organizationId, targetType: nameof(Role), targetId: role.Id,
            details: new { role.Name, permissions = dto.Permissions });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteRoleAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationRolesManage, cancellationToken);
        var role = await GetRoleEntityAsync(organizationId, roleId, cancellationToken);
        role.EnsureDeletable();
        await EnsureCanGrantAsync(organizationId, role.PermissionCodes, cancellationToken);
        if (await assignmentRepository.AnyForRoleAsync(role.Id, cancellationToken))
        {
            throw new BusinessRuleViolationException("The role is still assigned to members.");
        }

        roleRepository.Remove(role);
        audit.Write(AuditActions.RoleDeleted, organizationId: organizationId, targetType: nameof(Role), targetId: role.Id, details: new { role.Name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignRoleAsync(Guid organizationId, Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var actorId = await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationRolesManage, cancellationToken);
        await GetActiveOrganizationAsync(organizationId, cancellationToken);
        await EnsureNotSelfAsync(organizationId, actorId, userId, cancellationToken);

        var role = await GetRoleEntityAsync(organizationId, roleId, cancellationToken);
        await EnsureCanGrantAsync(organizationId, role.PermissionCodes, cancellationToken);
        var membership = await membershipRepository.GetAsync(organizationId, userId, cancellationToken)
            ?? throw new EntityNotFoundException("Member", userId);

        if (await assignmentRepository.ExistsAsync(userId, role.Id, organizationId, cancellationToken))
        {
            return;
        }

        assignmentRepository.Add(UserRoleAssignment.ForOrganization(membership, role, actorId, clock.UtcNow));
        audit.Write(AuditActions.RoleAssigned, organizationId: organizationId, targetType: nameof(User), targetId: userId,
            details: new { roleId = role.Id, role = role.Name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UnassignRoleAsync(Guid organizationId, Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var actorId = await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationRolesManage, cancellationToken);
        var assignment = await assignmentRepository.GetOrganizationAssignmentAsync(organizationId, assignmentId, cancellationToken)
            ?? throw new EntityNotFoundException("Role assignment", assignmentId);
        await EnsureNotSelfAsync(organizationId, actorId, assignment.UserId, cancellationToken);

        var role = await GetRoleEntityAsync(organizationId, assignment.RoleId, cancellationToken);
        await EnsureCanGrantAsync(organizationId, role.PermissionCodes, cancellationToken);

        assignmentRepository.Remove(assignment);
        audit.Write(AuditActions.RoleUnassigned, organizationId: organizationId, targetType: nameof(User), targetId: assignment.UserId,
            details: new { roleId = role.Id, role = role.Name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationApplicationDto>> ListApplicationsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationApplicationsManage, cancellationToken);
        var applications = await applicationRepository.ListGrantedToOrganizationAsync(organizationId, cancellationToken);
        return applications.Select(a => new OrganizationApplicationDto(
            a.Id,
            a.Name,
            a.Description,
            a.OwnerOrganizationId == organizationId,
            a.OrganizationAccesses.First(x => x.OrganizationId == organizationId).IsEnabled,
            a.IsActive)).ToList();
    }

    public async Task SetApplicationEnabledAsync(Guid organizationId, Guid applicationId, bool enabled, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationApplicationsManage, cancellationToken);
        var application = await applicationRepository.GetByIdAsync(applicationId, cancellationToken);
        if (application is null || application.OrganizationAccesses.All(a => a.OrganizationId != organizationId))
        {
            throw new EntityNotFoundException(nameof(ClientApplication), applicationId);
        }

        application.SetOrganizationAccessEnabled(organizationId, enabled, clock.UtcNow);
        audit.Write(AuditActions.ApplicationOrganizationAccessChanged, organizationId: organizationId, targetType: nameof(ClientApplication),
            targetId: applicationId, details: new { enabled });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<AuditEntryDto>> GetAuditAsync(Guid organizationId, AuditFilter filter, CancellationToken cancellationToken = default)
    {
        await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationSettingsManage, cancellationToken);
        var page = await auditRepository.GetPagedAsync(filter with { OrganizationId = organizationId }, cancellationToken);
        return page.Map(PlatformAdministrationService.ToDto);
    }

    private async Task<Organization> GetActiveOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null || !organization.IsActive)
        {
            throw new EntityNotFoundException(nameof(Organization), organizationId);
        }

        return organization;
    }

    private async Task<OrganizationMembership> GetManageableMembershipAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var actorId = await guard.RequireOrganizationPermissionAsync(organizationId, PermissionCatalog.OrganizationMembersManage, cancellationToken);
        await GetActiveOrganizationAsync(organizationId, cancellationToken);
        await EnsureNotSelfAsync(organizationId, actorId, userId, cancellationToken);

        var membership = await membershipRepository.GetAsync(organizationId, userId, cancellationToken);
        if (membership is null || membership.Status == MembershipStatus.Revoked)
        {
            throw new EntityNotFoundException("Member", userId);
        }

        // A member cannot act on another member holding permissions he does not have himself.
        var targetPermissions = await assignmentRepository.GetOrganizationPermissionsAsync(userId, organizationId, cancellationToken);
        await EnsureCanGrantAsync(organizationId, targetPermissions, cancellationToken);
        return membership;
    }

    private async Task<Role> GetRoleEntityAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken)
        => await roleRepository.GetOrganizationRoleAsync(organizationId, roleId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Role), roleId);

    private async Task EnsureCanGrantAsync(Guid organizationId, IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        var actorPermissions = await guard.GetOrganizationPermissionsAsync(organizationId, cancellationToken);
        var requested = permissions.ToList();
        if (!PrivilegeEscalationPolicy.CanGrant(actorPermissions, requested))
        {
            audit.Write(AuditActions.PrivilegeEscalationDenied, AuditOutcome.Denied, organizationId, details: new { permissions = requested });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new ForbiddenAccessException("You cannot grant or manage permissions you do not hold.");
        }
    }

    private async Task EnsureNotSelfAsync(Guid organizationId, Guid actorId, Guid targetUserId, CancellationToken cancellationToken)
    {
        if (!PrivilegeEscalationPolicy.CanManageAssignmentsOf(actorId, targetUserId))
        {
            audit.Write(AuditActions.PrivilegeEscalationDenied, AuditOutcome.Denied, organizationId, nameof(User), targetUserId,
                new { reason = "self_management" });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new ForbiddenAccessException("You cannot change your own membership or roles.");
        }
    }

    internal static OrganizationDto ToDto(Organization o) => new(o.Id, o.Name, o.Slug, o.Status, o.CreatedAt);

    internal static RoleDto ToDto(Role r) => new(r.Id, r.Name, r.Description, r.Scope, r.IsSystem, r.PermissionCodes.OrderBy(c => c).ToList());
}
