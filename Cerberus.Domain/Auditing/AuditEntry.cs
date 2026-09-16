using Cerberus.Domain.Common;

namespace Cerberus.Domain.Auditing;

/// <summary>
/// Immutable audit record. Invariants: never updated nor deleted by the application;
/// <see cref="Details"/> must never contain secrets (passwords, client secrets, tokens, keys).
/// </summary>
public sealed class AuditEntry
{
    public const int DetailsMaxLength = 4000;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public DateTime OccurredAt { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public AuditOutcome Outcome { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public string? TargetType { get; private set; }
    public string? TargetId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? Details { get; private set; }

    private AuditEntry()
    {
    }

    public static AuditEntry Create(
        string action,
        AuditOutcome outcome,
        Guid? actorUserId,
        Guid? organizationId,
        string? targetType,
        string? targetId,
        string? ipAddress,
        string? details,
        DateTime utcNow)
    {
        return new AuditEntry
        {
            OccurredAt = Check.Utc(utcNow, nameof(utcNow)),
            Action = Check.Required(action, nameof(Action), 100),
            Outcome = outcome,
            ActorUserId = actorUserId,
            OrganizationId = organizationId,
            TargetType = Check.Optional(targetType, nameof(TargetType), 100),
            TargetId = Check.Optional(targetId, nameof(TargetId), 100),
            IpAddress = Check.Optional(ipAddress, nameof(IpAddress), 64),
            Details = details is { Length: > DetailsMaxLength } ? details[..DetailsMaxLength] : details
        };
    }
}

public enum AuditOutcome
{
    Success = 0,
    Failure = 1,
    Denied = 2
}

public static class AuditActions
{
    public const string LoginSucceeded = "auth.login.succeeded";
    public const string LoginFailed = "auth.login.failed";
    public const string LockedOut = "auth.lockout";
    public const string Logout = "auth.logout";
    public const string PasswordResetRequested = "auth.password_reset.requested";
    public const string PasswordResetCompleted = "auth.password_reset.completed";
    public const string EmailConfirmed = "auth.email.confirmed";
    public const string SessionRevoked = "auth.session.revoked";

    public const string TokenIssued = "oidc.authorization.granted";
    public const string AuthorizationDenied = "oidc.authorization.denied";
    public const string TenantRejected = "oidc.tenant.rejected";
    public const string ConsentGranted = "oidc.consent.granted";
    public const string ConsentRevoked = "oidc.consent.revoked";

    public const string UserCreated = "admin.user.created";
    public const string UserUpdated = "admin.user.updated";
    public const string UserDisabled = "admin.user.disabled";
    public const string UserEnabled = "admin.user.enabled";
    public const string BootstrapAdministratorCreated = "admin.bootstrap.created";

    public const string OrganizationCreated = "admin.organization.created";
    public const string OrganizationUpdated = "admin.organization.updated";
    public const string OrganizationSuspended = "admin.organization.suspended";
    public const string OrganizationActivated = "admin.organization.activated";

    public const string MemberAdded = "org.member.added";
    public const string MemberSuspended = "org.member.suspended";
    public const string MemberResumed = "org.member.resumed";
    public const string MemberRemoved = "org.member.removed";
    public const string InvitationCreated = "org.invitation.created";
    public const string InvitationAccepted = "org.invitation.accepted";
    public const string InvitationRevoked = "org.invitation.revoked";

    public const string RoleCreated = "authz.role.created";
    public const string RoleUpdated = "authz.role.updated";
    public const string RoleDeleted = "authz.role.deleted";
    public const string RoleAssigned = "authz.role.assigned";
    public const string RoleUnassigned = "authz.role.unassigned";
    public const string PrivilegeEscalationDenied = "authz.escalation.denied";
    public const string CrossTenantAccessDenied = "authz.cross_tenant.denied";

    public const string ApplicationCreated = "admin.application.created";
    public const string ApplicationUpdated = "admin.application.updated";
    public const string ApplicationOrganizationAccessChanged = "admin.application.access_changed";
    public const string ClientCreated = "admin.client.created";
    public const string ClientUpdated = "admin.client.updated";
    public const string ClientSecretRotated = "admin.client.secret_rotated";
    public const string ClientDisabled = "admin.client.disabled";
    public const string ClientEnabled = "admin.client.enabled";
    public const string ScopeCreated = "admin.scope.created";
    public const string ScopeUpdated = "admin.scope.updated";
    public const string SigningKeyRotated = "security.signing_key.rotated";
    public const string SigningKeyCreated = "security.signing_key.created";
}
