using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;

namespace Cerberus.Application.DTOs;

public sealed record UserSummaryDto(Guid Id, string UserName, string Email, string FirstName, string LastName, UserStatus Status, bool EmailConfirmed, DateTime CreatedAt);

public sealed record UserDetailsDto(
    UserSummaryDto User,
    IReadOnlyList<UserMembershipDto> Memberships,
    IReadOnlyList<RoleAssignmentDto> PlatformRoles,
    bool IsLockedOut);

public sealed record UserMembershipDto(Guid OrganizationId, string OrganizationName, string OrganizationSlug, MembershipStatus Status);

public sealed record CreateUserDto(string FirstName, string LastName, string Email, string UserName, string Password, bool EmailConfirmed);

public sealed record UpdateUserDto(string FirstName, string LastName, string Email, string UserName);

public sealed record OrganizationDto(Guid Id, string Name, string Slug, OrganizationStatus Status, DateTime CreatedAt);

public sealed record CreateOrganizationDto(string Name, string Slug);

public sealed record MemberDto(
    Guid UserId,
    string UserName,
    string Email,
    string FullName,
    MembershipStatus Status,
    UserStatus UserStatus,
    IReadOnlyList<RoleAssignmentDto> Roles);

public sealed record RoleDto(Guid Id, string Name, string? Description, RoleScope Scope, bool IsSystem, IReadOnlyList<string> Permissions);

public sealed record SaveRoleDto(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record RoleAssignmentDto(Guid AssignmentId, Guid RoleId, string RoleName);

public sealed record InvitationDto(Guid Id, string Email, DateTime CreatedAt, DateTime ExpiresAt, DateTime? AcceptedAt, DateTime? RevokedAt);

public sealed record InvitationInfoDto(string OrganizationName, string Email, bool IsValid, bool AccountExists);

public sealed record RegisterFromInvitationDto(string Token, string FirstName, string LastName, string UserName, string Password);

public sealed record ApplicationDto(
    Guid Id,
    string Name,
    string? Description,
    Guid OwnerOrganizationId,
    bool IsActive,
    IReadOnlyList<ApplicationAccessDto> Organizations);

public sealed record ApplicationAccessDto(Guid OrganizationId, string OrganizationName, bool IsEnabled, bool IsOwner);

public sealed record SaveApplicationDto(string Name, string? Description, Guid OwnerOrganizationId);

public sealed record OrganizationApplicationDto(Guid ApplicationId, string Name, string? Description, bool IsOwner, bool IsEnabled, bool IsApplicationActive);

public sealed record OidcClientDto(
    Guid Id,
    Guid ApplicationId,
    string ClientId,
    string DisplayName,
    ClientType ClientType,
    ConsentPolicy ConsentPolicy,
    TimeSpan? ConsentLifetime,
    bool IsActive,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> AllowedScopes);

public sealed record CreateOidcClientDto(
    Guid ApplicationId,
    string ClientId,
    string DisplayName,
    ClientType ClientType,
    ConsentPolicy ConsentPolicy,
    int? ConsentLifetimeDays,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> AllowedScopes);

public sealed record UpdateOidcClientDto(
    string DisplayName,
    ConsentPolicy ConsentPolicy,
    int? ConsentLifetimeDays,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> AllowedScopes);

/// <summary>The plain client secret is only returned once, at creation or rotation.</summary>
public sealed record ClientSecretDto(string ClientId, string? ClientSecret);

public sealed record ApiScopeDto(Guid Id, string Name, string DisplayName, string? Description, string Resource);

public sealed record SaveApiScopeDto(string Name, string DisplayName, string? Description, string Resource);

public sealed record AuditEntryDto(
    Guid Id,
    DateTime OccurredAt,
    string Action,
    AuditOutcome Outcome,
    Guid? ActorUserId,
    Guid? OrganizationId,
    string? TargetType,
    string? TargetId,
    string? IpAddress,
    string? Details);

public sealed record ConsentDto(Guid Id, string ClientDisplayName, string OrganizationName, IReadOnlyList<string> Scopes, DateTime GrantedAt, DateTime? ExpiresAt);

public sealed record AdministrationAccessDto(IReadOnlySet<string> PlatformPermissions, IReadOnlyList<OrganizationDto> AdministrableOrganizations);
