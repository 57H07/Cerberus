using System.ComponentModel.DataAnnotations;
using Cerberus.Application.DTOs;
using Cerberus.Application.Oidc;

namespace Cerberus.Web.ViewModels;

public sealed class LoginViewModel
{
    [Required, Display(Name = "Login or email"), StringLength(254)]
    public string Login { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(256)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public sealed class ForgotPasswordViewModel
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(256, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password)), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class RegisterFromInvitationViewModel
{
    [Required]
    public string Token { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string OrganizationName { get; set; } = string.Empty;

    [Required, StringLength(100), Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100), Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, StringLength(64, MinimumLength = 3), RegularExpression(@"^[a-zA-Z0-9._-]+$"), Display(Name = "Login")]
    public string UserName { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(256, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password)), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed record InvitationViewModel(string Token, InvitationInfoDto Invitation, bool IsAuthenticated);

public sealed record MyAccountViewModel(
    UserProfileDto Profile,
    IReadOnlyList<SessionDto> Sessions,
    Guid? CurrentSessionId,
    IReadOnlyList<ConsentDto> Consents,
    IReadOnlyList<UserMembershipDto> Memberships);

public sealed record SelectOrganizationViewModel(IReadOnlyList<OrganizationOptionDto> Organizations, IReadOnlyList<KeyValuePair<string, string>> Parameters);

public sealed record ConsentViewModel(ConsentPromptDto Consent, IReadOnlyList<KeyValuePair<string, string>> Parameters);

public sealed record EndSessionViewModel(IReadOnlyList<KeyValuePair<string, string>> Parameters);
