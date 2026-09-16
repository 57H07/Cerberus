using Cerberus.Application.Authorization;
using Cerberus.Application.Common;
using Cerberus.Application.DTOs;
using Cerberus.Application.Exceptions;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Security;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;
using Cerberus.Domain.Common;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;

namespace Cerberus.Application.Services;

/// <summary>Platform administration of organizations, organization administrators, platform roles and the global audit log.</summary>
public interface IPlatformAdministrationService
{
    Task<PagedResult<OrganizationDto>> ListOrganizationsAsync(PagedFilter filter, CancellationToken cancellationToken = default);
    Task<OrganizationDto> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<Guid> CreateOrganizationAsync(CreateOrganizationDto dto, CancellationToken cancellationToken = default);
    Task RenameOrganizationAsync(Guid organizationId, string name, CancellationToken cancellationToken = default);
    Task SuspendOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task ActivateOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemberDto>> ListOrganizationAdministratorsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task AddOrganizationAdministratorAsync(Guid organizationId, string login, CancellationToken cancellationToken = default);
    Task RemoveOrganizationAdministratorAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDto>> ListPlatformRolesAsync(CancellationToken cancellationToken = default);
    Task<Guid> CreatePlatformRoleAsync(SaveRoleDto dto, CancellationToken cancellationToken = default);
    Task UpdatePlatformRoleAsync(Guid roleId, SaveRoleDto dto, CancellationToken cancellationToken = default);
    Task AssignPlatformRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);
    Task UnassignPlatformRoleAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    Task<PagedResult<AuditEntryDto>> GetAuditAsync(AuditFilter filter, CancellationToken cancellationToken = default);
}

public class PlatformAdministrationService(
    IAccessGuard guard,
    IOrganizationRepository organizationRepository,
    IMembershipRepository membershipRepository,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IRoleAssignmentRepository assignmentRepository,
    IAuditRepository auditRepository,
    ITokenRevocationService tokenRevocation,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock) : IPlatformAdministrationService
{
    public async Task<PagedResult<OrganizationDto>> ListOrganizationsAsync(PagedFilter filter, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformOrganizationsManage, cancellationToken);
        var page = await organizationRepository.GetPagedAsync(filter, cancellationToken);
        return page.Map(OrganizationManagementService.ToDto);
    }

    public async Task<OrganizationDto> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformOrganizationsManage, cancellationToken);
        return OrganizationManagementService.ToDto(await GetOrganizationEntityAsync(organizationId, cancellationToken));
    }

    public async Task<Guid> CreateOrganizationAsync(CreateOrganizationDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformOrganizationsManage, cancellationToken);
        var now = clock.UtcNow;
        var organization = Organization.Create(dto.Name, dto.Slug, now);
        if (await organizationRepository.GetBySlugAsync(organization.Slug, cancellationToken) is not null)
        {
            throw new DuplicateEntityException("organization", "slug");
        }

        organizationRepository.Add(organization);
        roleRepository.Add(CreateOrganizationAdministratorRole(organization.Id, now));
        audit.Write(AuditActions.OrganizationCreated, organizationId: organization.Id, targetType: nameof(Organization), targetId: organization.Id,
            details: new { organization.Name, organization.Slug });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return organization.Id;
    }

    public async Task RenameOrganizationAsync(Guid organizationId, string name, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformOrganizationsManage, cancellationToken);
        var organization = await GetOrganizationEntityAsync(organizationId, cancellationToken);
        organization.Rename(name, clock.UtcNow);
        audit.Write(AuditActions.OrganizationUpdated, organizationId: organizationId, targetType: nameof(Organization), targetId: organizationId,
            details: new { name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SuspendOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformOrganizationsManage, cancellationToken);
        var organization = await GetOrganizationEntityAsync(organizationId, cancellationToken);
        organization.Suspend(clock.UtcNow);
        audit.Write(AuditActions.OrganizationSuspended, organizationId: organizationId, targetType: nameof(Organization), targetId: organizationId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await tokenRevocation.RevokeOrganizationAsync(organizationId, cancellationToken);
    }

    public async Task ActivateOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformOrganizationsManage, cancellationToken);
        var organization = await GetOrganizationEntityAsync(organizationId, cancellationToken);
        organization.Activate(clock.UtcNow);
        audit.Write(AuditActions.OrganizationActivated, organizationId: organizationId, targetType: nameof(Organization), targetId: organizationId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemberDto>> ListOrganizationAdministratorsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformOrganizationsManage, cancellationToken);
        var role = await GetOrganizationAdministratorRoleAsync(organizationId, cancellationToken);
        var assignments = (await assignmentRepository.ListOrganizationAssignmentsAsync(organizationId, null, cancellationToken))
            .Where(a => a.RoleId == role.Id).ToList();
        var users = await userRepository.GetByIdsAsync(assignments.Select(a => a.UserId), cancellationToken);
        var memberships = await membershipRepository.ListByOrganizationAsync(organizationId, cancellationToken);

        return users.Select(u => new MemberDto(
                u.Id, u.UserName, u.Email, $"{u.FirstName} {u.LastName}",
                memberships.First(m => m.UserId == u.Id).Status, u.Status,
                assignments.Where(a => a.UserId == u.Id).Select(a => new RoleAssignmentDto(a.AssignmentId, a.RoleId, a.RoleName)).ToList()))
            .OrderBy(m => m.FullName)
            .ToList();
    }

    public async Task AddOrganizationAdministratorAsync(Guid organizationId, string login, CancellationToken cancellationToken = default)
    {
        var actorId = await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformOrganizationsManage, cancellationToken);
        await GetOrganizationEntityAsync(organizationId, cancellationToken);
        var normalized = IdentityNormalizer.Normalize(login ?? string.Empty);
        var user = (login ?? string.Empty).Contains('@')
            ? await userRepository.FindByNormalizedEmailAsync(normalized, cancellationToken)
            : await userRepository.FindByNormalizedUserNameAsync(normalized, cancellationToken);
        if (user is null)
        {
            throw new EntityNotFoundException(nameof(User), login ?? string.Empty);
        }

        var now = clock.UtcNow;
        var membership = await membershipRepository.GetAsync(organizationId, user.Id, cancellationToken);
        if (membership is null)
        {
            membership = OrganizationMembership.Create(organizationId, user.Id, now);
            membershipRepository.Add(membership);
            audit.Write(AuditActions.MemberAdded, organizationId: organizationId, targetType: nameof(User), targetId: user.Id);
        }
        else if (!membership.IsActive)
        {
            membership.Reactivate(now);
        }

        var role = await GetOrganizationAdministratorRoleAsync(organizationId, cancellationToken);
        if (!await assignmentRepository.ExistsAsync(user.Id, role.Id, organizationId, cancellationToken))
        {
            assignmentRepository.Add(UserRoleAssignment.ForOrganization(membership, role, actorId, now));
            audit.Write(AuditActions.RoleAssigned, organizationId: organizationId, targetType: nameof(User), targetId: user.Id,
                details: new { roleId = role.Id, role = role.Name });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveOrganizationAdministratorAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformOrganizationsManage, cancellationToken);
        var role = await GetOrganizationAdministratorRoleAsync(organizationId, cancellationToken);
        var assignment = (await assignmentRepository.ListOrganizationAssignmentsAsync(organizationId, userId, cancellationToken))
            .FirstOrDefault(a => a.RoleId == role.Id)
            ?? throw new EntityNotFoundException("Role assignment", userId);
        var entity = await assignmentRepository.GetOrganizationAssignmentAsync(organizationId, assignment.AssignmentId, cancellationToken);
        assignmentRepository.Remove(entity!);
        audit.Write(AuditActions.RoleUnassigned, organizationId: organizationId, targetType: nameof(User), targetId: userId,
            details: new { roleId = role.Id, role = role.Name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleDto>> ListPlatformRolesAsync(CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformRolesManage, cancellationToken);
        return (await roleRepository.ListPlatformRolesAsync(cancellationToken)).Select(OrganizationManagementService.ToDto).ToList();
    }

    public async Task<Guid> CreatePlatformRoleAsync(SaveRoleDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformRolesManage, cancellationToken);
        await EnsureCanGrantAsync(dto.Permissions, cancellationToken);
        var role = Role.CreatePlatformRole(dto.Name, dto.Description, false, clock.UtcNow);
        role.SetPermissions(dto.Permissions, clock.UtcNow);
        roleRepository.Add(role);
        audit.Write(AuditActions.RoleCreated, targetType: nameof(Role), targetId: role.Id, details: new { role.Name, permissions = dto.Permissions });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return role.Id;
    }

    public async Task UpdatePlatformRoleAsync(Guid roleId, SaveRoleDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformRolesManage, cancellationToken);
        var role = await roleRepository.GetPlatformRoleAsync(roleId, cancellationToken) ?? throw new EntityNotFoundException(nameof(Role), roleId);
        await EnsureCanGrantAsync(role.PermissionCodes.Concat(dto.Permissions), cancellationToken);
        role.Update(dto.Name, dto.Description, clock.UtcNow);
        role.SetPermissions(dto.Permissions, clock.UtcNow);
        audit.Write(AuditActions.RoleUpdated, targetType: nameof(Role), targetId: role.Id, details: new { role.Name, permissions = dto.Permissions });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignPlatformRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var actorId = await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformRolesManage, cancellationToken);
        EnsureNotSelf(actorId, userId);
        var role = await roleRepository.GetPlatformRoleAsync(roleId, cancellationToken) ?? throw new EntityNotFoundException(nameof(Role), roleId);
        await EnsureCanGrantAsync(role.PermissionCodes, cancellationToken);
        var user = await userRepository.GetByIdAsync(userId, cancellationToken) ?? throw new EntityNotFoundException(nameof(User), userId);

        if (await assignmentRepository.ExistsAsync(user.Id, role.Id, null, cancellationToken))
        {
            return;
        }

        assignmentRepository.Add(UserRoleAssignment.ForPlatform(user.Id, role, actorId, clock.UtcNow));
        audit.Write(AuditActions.RoleAssigned, targetType: nameof(User), targetId: user.Id, details: new { roleId = role.Id, role = role.Name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UnassignPlatformRoleAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var actorId = await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformRolesManage, cancellationToken);
        var assignment = await assignmentRepository.GetPlatformAssignmentAsync(assignmentId, cancellationToken)
            ?? throw new EntityNotFoundException("Role assignment", assignmentId);
        EnsureNotSelf(actorId, assignment.UserId);
        var role = await roleRepository.GetPlatformRoleAsync(assignment.RoleId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Role), assignment.RoleId);
        await EnsureCanGrantAsync(role.PermissionCodes, cancellationToken);

        assignmentRepository.Remove(assignment);
        audit.Write(AuditActions.RoleUnassigned, targetType: nameof(User), targetId: assignment.UserId, details: new { roleId = role.Id, role = role.Name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<AuditEntryDto>> GetAuditAsync(AuditFilter filter, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformAuditRead, cancellationToken);
        return (await auditRepository.GetPagedAsync(filter, cancellationToken)).Map(ToDto);
    }

    public static Role CreateOrganizationAdministratorRole(Guid organizationId, DateTime utcNow)
    {
        var role = Role.CreateOrganizationRole(organizationId, Role.OrganizationAdministratorName, "Full administration of the organization", true, utcNow);
        role.InitializeSystemPermissions(PermissionCatalog.CodesFor(RoleScope.Organization), utcNow);
        return role;
    }

    internal static AuditEntryDto ToDto(AuditEntry a)
        => new(a.Id, a.OccurredAt, a.Action, a.Outcome, a.ActorUserId, a.OrganizationId, a.TargetType, a.TargetId, a.IpAddress, a.Details);

    private async Task<Organization> GetOrganizationEntityAsync(Guid organizationId, CancellationToken cancellationToken)
        => await organizationRepository.GetByIdAsync(organizationId, cancellationToken) ?? throw new EntityNotFoundException(nameof(Organization), organizationId);

    private async Task<Role> GetOrganizationAdministratorRoleAsync(Guid organizationId, CancellationToken cancellationToken)
        => await roleRepository.FindOrganizationRoleByNameAsync(organizationId, Role.OrganizationAdministratorName, cancellationToken)
            ?? throw new BusinessRuleViolationException("The organization has no administrator role.");

    private async Task EnsureCanGrantAsync(IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        var actorPermissions = await guard.GetPlatformPermissionsAsync(cancellationToken);
        var requested = permissions.ToList();
        if (!PrivilegeEscalationPolicy.CanGrant(actorPermissions, requested))
        {
            audit.Write(AuditActions.PrivilegeEscalationDenied, AuditOutcome.Denied, details: new { permissions = requested });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new ForbiddenAccessException("You cannot grant or manage permissions you do not hold.");
        }
    }

    private static void EnsureNotSelf(Guid actorId, Guid userId)
    {
        if (!PrivilegeEscalationPolicy.CanManageAssignmentsOf(actorId, userId))
        {
            throw new ForbiddenAccessException("You cannot change your own roles.");
        }
    }
}
