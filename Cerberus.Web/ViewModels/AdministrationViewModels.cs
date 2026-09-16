using System.ComponentModel.DataAnnotations;
using Cerberus.Application.Common;
using Cerberus.Application.DTOs;
using Cerberus.Domain.Clients;

namespace Cerberus.Web.ViewModels;

public sealed class UserFormViewModel
{
    public Guid? Id { get; set; }

    [Required, StringLength(100), Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100), Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(64, MinimumLength = 3), RegularExpression(@"^[a-zA-Z0-9._-]+$"), Display(Name = "Login")]
    public string UserName { get; set; } = string.Empty;

    [DataType(DataType.Password), StringLength(256, MinimumLength = 12), Display(Name = "Initial password")]
    public string? Password { get; set; }

    [Display(Name = "Email already verified")]
    public bool EmailConfirmed { get; set; }
}

public sealed record UserDetailsViewModel(UserDetailsDto Details, IReadOnlyList<RoleDto> PlatformRoles);

public sealed record OrganizationDetailsViewModel(OrganizationDto Organization, IReadOnlyList<MemberDto> Administrators);

public sealed class OrganizationFormViewModel
{
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression(@"^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", ErrorMessage = "3-63 lower-case letters, digits and inner hyphens.")]
    public string Slug { get; set; } = string.Empty;
}

public sealed class ApplicationFormViewModel
{
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required, Display(Name = "Owner organization")]
    public Guid OwnerOrganizationId { get; set; }

    public IReadOnlyList<OrganizationDto> Organizations { get; set; } = [];
}

public sealed record ApplicationDetailsViewModel(ApplicationDto Application, IReadOnlyList<OidcClientDto> Clients);

public sealed class ClientFormViewModel
{
    public Guid? Id { get; set; }

    public Guid ApplicationId { get; set; }

    [Required, StringLength(100), RegularExpression(@"^[a-zA-Z0-9._-]{3,100}$"), Display(Name = "Client ID")]
    public string ClientId { get; set; } = string.Empty;

    [Required, StringLength(200), Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "Client type")]
    public ClientType ClientType { get; set; } = ClientType.Confidential;

    [Display(Name = "Consent policy")]
    public ConsentPolicy ConsentPolicy { get; set; } = ConsentPolicy.Remembered;

    [Range(1, 3650), Display(Name = "Consent lifetime (days, empty = until revoked)")]
    public int? ConsentLifetimeDays { get; set; }

    [Required, Display(Name = "Redirect URIs (one per line)")]
    public string RedirectUris { get; set; } = string.Empty;

    [Display(Name = "Post-logout redirect URIs (one per line)")]
    public string? PostLogoutRedirectUris { get; set; }

    [Display(Name = "Allowed scopes")]
    public List<string> AllowedScopes { get; set; } = [Scopes.OpenId];

    public IReadOnlyList<string> AvailableScopes { get; set; } = [];
}

public sealed class ScopeFormViewModel
{
    [Required, RegularExpression(@"^[a-z][a-z0-9._:-]{1,99}$")]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(200), Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required, RegularExpression(@"^[a-zA-Z0-9._:-]{3,100}$"), Display(Name = "Resource (access token audience)")]
    public string Resource { get; set; } = string.Empty;
}

public sealed class RoleFormViewModel
{
    public Guid? Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public List<string> Permissions { get; set; } = [];

    public IReadOnlyList<Domain.Authorization.Permission> AvailablePermissions { get; set; } = [];
}

public sealed record AuditViewModel(PagedResult<AuditEntryDto> Page, string? ActionPrefix);

public sealed record OrganizationHomeViewModel(OrganizationDto Organization, IReadOnlySet<string> Permissions);

public sealed record MembersViewModel(
    OrganizationDto Organization,
    IReadOnlyList<MemberDto> Members,
    IReadOnlyList<RoleDto> Roles,
    IReadOnlyList<InvitationDto>? Invitations);

public sealed record OrganizationRolesViewModel(OrganizationDto Organization, IReadOnlyList<RoleDto> Roles);

public sealed record OrganizationApplicationsViewModel(OrganizationDto Organization, IReadOnlyList<OrganizationApplicationDto> Applications);
