using System.Net;
using Cerberus.Application.Common;
using Cerberus.Application.DTOs;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Security;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Common;
using Cerberus.Domain.Sessions;
using Cerberus.Domain.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cerberus.Application.Services;

public class UserAuthenticationService(
    IUserRepository userRepository,
    IUserSessionRepository sessionRepository,
    IIdentityService identityService,
    ITokenRevocationService tokenRevocationService,
    IEmailSender emailSender,
    ILinkBuilder linkBuilder,
    IAuditWriter audit,
    IActorContext actor,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<SessionOptions> sessionOptions,
    ILogger<UserAuthenticationService> logger) : IUserAuthenticationService
{
    private readonly SessionOptions _sessionOptions = sessionOptions.Value;

    public async Task<LoginResultDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await FindByLoginAsync(request.Login, cancellationToken);
        if (user is null)
        {
            identityService.SimulatePasswordCheck(request.Password);
            audit.Write(AuditActions.LoginFailed, AuditOutcome.Failure, details: new { reason = "unknown_login" });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new LoginResultDto(LoginStatus.InvalidCredentials);
        }

        var check = await identityService.CheckPasswordAsync(user, request.Password, cancellationToken);
        switch (check)
        {
            case PasswordCheckResult.LockedOut:
                audit.Write(AuditActions.LockedOut, AuditOutcome.Denied, targetType: nameof(User), targetId: user.Id, actorUserId: user.Id);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new LoginResultDto(LoginStatus.LockedOut);
            case PasswordCheckResult.InvalidPassword:
                audit.Write(AuditActions.LoginFailed, AuditOutcome.Failure, targetType: nameof(User), targetId: user.Id, actorUserId: user.Id,
                    details: new { reason = "invalid_password" });
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new LoginResultDto(LoginStatus.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            audit.Write(AuditActions.LoginFailed, AuditOutcome.Denied, targetType: nameof(User), targetId: user.Id, actorUserId: user.Id,
                details: new { reason = "account_disabled" });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new LoginResultDto(LoginStatus.AccountDisabled);
        }

        var session = UserSession.Start(user.Id, _sessionOptions.IdleTimeout, _sessionOptions.AbsoluteLifetime,
            actor.IpAddress, actor.UserAgent, clock.UtcNow);
        sessionRepository.Add(session);
        audit.Write(AuditActions.LoginSucceeded, targetType: nameof(User), targetId: user.Id, actorUserId: user.Id,
            details: new { sessionId = session.Id });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResultDto(LoginStatus.Succeeded, user.Id, session.Id, user.SecurityStamp);
    }

    public async Task<bool> ValidateSessionAsync(Guid userId, Guid sessionId, string securityStamp, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != userId || !session.IsActive(now))
        {
            return false;
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive || !string.Equals(user.SecurityStamp, securityStamp, StringComparison.Ordinal))
        {
            session.Revoke(user is { IsActive: true } ? "security_stamp_changed" : "account_disabled", now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return false;
        }

        if (now - session.LastSeenAt >= _sessionOptions.RefreshInterval)
        {
            session.Refresh(_sessionOptions.IdleTimeout, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is not null && session.UserId == userId)
        {
            session.Revoke("logout", clock.UtcNow);
        }

        audit.Write(AuditActions.Logout, targetType: nameof(UserSession), targetId: sessionId, actorUserId: userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var sessions = await sessionRepository.ListActiveForUserAsync(userId, clock.UtcNow, cancellationToken);
        return sessions.Select(s => new SessionDto(s.Id, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.IpAddress, s.UserAgent)).ToList();
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != userId)
        {
            return;
        }

        session.Revoke("revoked_by_user", clock.UtcNow);
        audit.Write(AuditActions.SessionRevoked, targetType: nameof(UserSession), targetId: sessionId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeEverythingAsync(Guid userId, string reason, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        foreach (var session in await sessionRepository.ListActiveForUserAsync(userId, now, cancellationToken))
        {
            session.Revoke(reason, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await tokenRevocationService.RevokeUserAsync(userId, cancellationToken);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        if (!User.IsValidEmail(email))
        {
            return;
        }

        var user = await userRepository.FindByNormalizedEmailAsync(IdentityNormalizer.Normalize(email), cancellationToken);
        if (user is null || !user.IsActive)
        {
            logger.LogInformation("Password reset requested for an unknown or disabled account.");
            return;
        }

        var token = await identityService.GeneratePasswordResetTokenAsync(user, cancellationToken);
        var link = linkBuilder.PasswordReset(user.Id, token);
        audit.Write(AuditActions.PasswordResetRequested, targetType: nameof(User), targetId: user.Id, actorUserId: user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            user.Email,
            "Cerberus - password reset",
            $"<p>Hello {WebUtility.HtmlEncode(user.FirstName)},</p><p>To choose a new password, follow <a href=\"{WebUtility.HtmlEncode(link)}\">this link</a>. It expires shortly.</p><p>If you did not request it, ignore this message.</p>",
            cancellationToken);
    }

    public async Task<OperationResult> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return OperationResult.Failure("The reset link is invalid or has expired.");
        }

        var result = await identityService.ResetPasswordAsync(user, token, newPassword, cancellationToken);
        if (!result.Succeeded)
        {
            return result;
        }

        audit.Write(AuditActions.PasswordResetCompleted, targetType: nameof(User), targetId: user.Id, actorUserId: user.Id);
        await RevokeEverythingAsync(user.Id, "password_reset", cancellationToken);
        return OperationResult.Success;
    }

    public async Task SendEmailConfirmationAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || user.EmailConfirmed || !user.IsActive)
        {
            return;
        }

        var token = await identityService.GenerateEmailConfirmationTokenAsync(user, cancellationToken);
        var link = linkBuilder.EmailConfirmation(user.Id, token);
        await emailSender.SendAsync(
            user.Email,
            "Cerberus - confirm your email address",
            $"<p>Hello {WebUtility.HtmlEncode(user.FirstName)},</p><p>Please confirm your email address by following <a href=\"{WebUtility.HtmlEncode(link)}\">this link</a>.</p>",
            cancellationToken);
    }

    public async Task<OperationResult> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return OperationResult.Failure("The confirmation link is invalid.");
        }

        var result = await identityService.ConfirmEmailAsync(user, token, cancellationToken);
        if (result.Succeeded)
        {
            audit.Write(AuditActions.EmailConfirmed, targetType: nameof(User), targetId: user.Id, actorUserId: user.Id);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        return user is null ? null : new UserProfileDto(user.Id, user.UserName, user.Email, user.EmailConfirmed, user.FirstName, user.LastName);
    }

    private Task<User?> FindByLoginAsync(string login, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return Task.FromResult<User?>(null);
        }

        var normalized = IdentityNormalizer.Normalize(login);
        return login.Contains('@')
            ? userRepository.FindByNormalizedEmailAsync(normalized, cancellationToken)
            : userRepository.FindByNormalizedUserNameAsync(normalized, cancellationToken);
    }
}
