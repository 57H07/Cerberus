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
using Cerberus.Domain.Users;

namespace Cerberus.Application.Services;

public interface IUserAdministrationService
{
    Task<PagedResult<UserSummaryDto>> ListAsync(PagedFilter filter, CancellationToken cancellationToken = default);
    Task<UserDetailsDto> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid userId, UpdateUserDto dto, CancellationToken cancellationToken = default);
    Task DisableAsync(Guid userId, CancellationToken cancellationToken = default);
    Task EnableAsync(Guid userId, CancellationToken cancellationToken = default);
}

public class UserAdministrationService(
    IAccessGuard guard,
    IUserRepository userRepository,
    IMembershipRepository membershipRepository,
    IOrganizationRepository organizationRepository,
    IRoleAssignmentRepository assignmentRepository,
    IIdentityService identityService,
    IUserAuthenticationService authenticationService,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock) : IUserAdministrationService
{
    public async Task<PagedResult<UserSummaryDto>> ListAsync(PagedFilter filter, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformUsersManage, cancellationToken);
        var page = await userRepository.GetPagedAsync(filter, cancellationToken);
        return page.Map(ToSummary);
    }

    public async Task<UserDetailsDto> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformUsersManage, cancellationToken);
        var user = await GetUserAsync(userId, cancellationToken);
        var memberships = await membershipRepository.ListByUserAsync(userId, cancellationToken);
        var organizations = await organizationRepository.GetByIdsAsync(memberships.Select(m => m.OrganizationId), cancellationToken);
        var platformRoles = await assignmentRepository.ListPlatformAssignmentsAsync(userId, cancellationToken);

        return new UserDetailsDto(
            ToSummary(user),
            memberships.Join(organizations, m => m.OrganizationId, o => o.Id, (m, o) => new UserMembershipDto(o.Id, o.Name, o.Slug, m.Status)).ToList(),
            platformRoles.Select(a => new RoleAssignmentDto(a.AssignmentId, a.RoleId, a.RoleName)).ToList(),
            user.IsLockedOut(clock.UtcNow));
    }

    public async Task<Guid> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformUsersManage, cancellationToken);
        var now = clock.UtcNow;
        var user = User.Create(dto.FirstName, dto.LastName, dto.Email, dto.UserName, now);
        if (dto.EmailConfirmed)
        {
            user.ConfirmEmail(now);
        }

        await EnsureUniqueAsync(user, cancellationToken);
        var result = await identityService.CreateUserAsync(user, dto.Password, cancellationToken);
        if (!result.Succeeded)
        {
            throw new BusinessRuleViolationException(string.Join(" ", result.Errors));
        }

        audit.Write(AuditActions.UserCreated, targetType: nameof(User), targetId: user.Id, details: new { user.UserName, user.Email });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!user.EmailConfirmed)
        {
            await authenticationService.SendEmailConfirmationAsync(user.Id, cancellationToken);
        }

        return user.Id;
    }

    public async Task UpdateAsync(Guid userId, UpdateUserDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformUsersManage, cancellationToken);
        var user = await GetUserAsync(userId, cancellationToken);
        var now = clock.UtcNow;
        user.UpdateProfile(dto.FirstName, dto.LastName, now);
        user.ChangeEmail(dto.Email, now);
        user.ChangeUserName(dto.UserName, now);
        await EnsureUniqueAsync(user, cancellationToken);

        audit.Write(AuditActions.UserUpdated, targetType: nameof(User), targetId: user.Id, details: new { user.UserName, user.Email });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DisableAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var actorId = await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformUsersManage, cancellationToken);
        if (actorId == userId)
        {
            throw new ForbiddenAccessException("You cannot disable your own account.");
        }

        var user = await GetUserAsync(userId, cancellationToken);
        await EnsureActorCoversTargetAsync(userId, cancellationToken);
        user.Disable(clock.UtcNow);
        audit.Write(AuditActions.UserDisabled, targetType: nameof(User), targetId: user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await authenticationService.RevokeEverythingAsync(user.Id, "account_disabled", cancellationToken);
    }

    public async Task EnableAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformUsersManage, cancellationToken);
        var user = await GetUserAsync(userId, cancellationToken);
        user.Enable(clock.UtcNow);
        audit.Write(AuditActions.UserEnabled, targetType: nameof(User), targetId: user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureActorCoversTargetAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        // Disabling a more privileged platform user would be a way to take over the platform.
        var actorPermissions = await guard.GetPlatformPermissionsAsync(cancellationToken);
        var targetPermissions = await assignmentRepository.GetPlatformPermissionsAsync(targetUserId, cancellationToken);
        if (!PrivilegeEscalationPolicy.CanGrant(actorPermissions, targetPermissions))
        {
            audit.Write(AuditActions.PrivilegeEscalationDenied, AuditOutcome.Denied, targetType: nameof(User), targetId: targetUserId);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new ForbiddenAccessException("You cannot manage a user holding permissions you do not have.");
        }
    }

    private async Task EnsureUniqueAsync(User user, CancellationToken cancellationToken)
    {
        var byEmail = await userRepository.FindByNormalizedEmailAsync(user.NormalizedEmail, cancellationToken);
        if (byEmail is not null && byEmail.Id != user.Id)
        {
            throw new DuplicateEntityException("user", "email address");
        }

        var byLogin = await userRepository.FindByNormalizedUserNameAsync(user.NormalizedUserName, cancellationToken);
        if (byLogin is not null && byLogin.Id != user.Id)
        {
            throw new DuplicateEntityException("user", "login");
        }
    }

    private async Task<User> GetUserAsync(Guid userId, CancellationToken cancellationToken)
        => await userRepository.GetByIdAsync(userId, cancellationToken) ?? throw new EntityNotFoundException(nameof(User), userId);

    internal static UserSummaryDto ToSummary(User u) => new(u.Id, u.UserName, u.Email, u.FirstName, u.LastName, u.Status, u.EmailConfirmed, u.CreatedAt);
}
