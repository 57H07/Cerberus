using Cerberus.Application.DTOs;
using Cerberus.Application.Interfaces.Security;

namespace Cerberus.Application.Interfaces.Services;

public interface IUserAuthenticationService
{
    Task<LoginResultDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Validates a cookie session on each request and slides its expiration.</summary>
    Task<bool> ValidateSessionAsync(Guid userId, Guid sessionId, string securityStamp, CancellationToken cancellationToken = default);

    Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionDto>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Revokes every session and every OIDC token of the user (password reset, account disabled...).</summary>
    Task RevokeEverythingAsync(Guid userId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Always completes the same way whether the email exists or not.</summary>
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);

    Task<OperationResult> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken = default);

    Task SendEmailConfirmationAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<OperationResult> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default);

    Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
}
