using Cerberus.Domain.Clients;
using Cerberus.Domain.Users;

namespace Cerberus.Application.Interfaces.Security;

public sealed record OperationResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static readonly OperationResult Success = new(true, []);

    public static OperationResult Failure(params string[] errors) => new(false, errors);
}

public enum PasswordCheckResult
{
    Success = 0,
    InvalidPassword = 1,
    LockedOut = 2
}

/// <summary>
/// Credential operations delegated to ASP.NET Core Identity (hashing, lockout, security stamp, data-protection tokens).
/// Implementations persist the user immediately.
/// </summary>
public interface IIdentityService
{
    Task<OperationResult> CreateUserAsync(User user, string password, CancellationToken cancellationToken = default);
    Task<PasswordCheckResult> CheckPasswordAsync(User user, string password, CancellationToken cancellationToken = default);

    /// <summary>Consumes the same time as a real password verification, to prevent user enumeration by timing.</summary>
    void SimulatePasswordCheck(string password);

    Task<OperationResult> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default);
    Task<OperationResult> ChangePasswordAsync(User user, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<string> GeneratePasswordResetTokenAsync(User user, CancellationToken cancellationToken = default);
    Task<OperationResult> ResetPasswordAsync(User user, string token, string newPassword, CancellationToken cancellationToken = default);
    Task<string> GenerateEmailConfirmationTokenAsync(User user, CancellationToken cancellationToken = default);
    Task<OperationResult> ConfirmEmailAsync(User user, string token, CancellationToken cancellationToken = default);
}

/// <summary>Revokes authorizations and tokens held by the OIDC library.</summary>
public interface ITokenRevocationService
{
    Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task RevokeUserInOrganizationAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task RevokeOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task RevokeClientAsync(string clientId, CancellationToken cancellationToken = default);
    Task RevokeAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
}

/// <summary>Projects domain client and scope configuration into the OIDC library store.</summary>
public interface IOidcClientRegistry
{
    Task CreateAsync(OidcClient client, string? clientSecret, CancellationToken cancellationToken = default);
    Task UpdateAsync(OidcClient client, CancellationToken cancellationToken = default);
    Task SetSecretAsync(OidcClient client, string clientSecret, CancellationToken cancellationToken = default);
    Task SyncScopeAsync(ApiScope scope, CancellationToken cancellationToken = default);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}

/// <summary>Builds absolute links sent by email. Implemented by the web layer.</summary>
public interface ILinkBuilder
{
    string PasswordReset(Guid userId, string token);
    string EmailConfirmation(Guid userId, string token);
    string Invitation(string token);
}
