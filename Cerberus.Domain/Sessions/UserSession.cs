using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Sessions;

/// <summary>
/// Server-side record of an interactive login session (exposed as the <c>sid</c> claim).
/// Invariants: sliding expiration never goes beyond the absolute expiration; a revoked session is terminal.
/// </summary>
public sealed class UserSession : Entity
{
    public Guid UserId { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime AbsoluteExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    private UserSession()
    {
    }

    private UserSession(DateTime utcNow) : base(utcNow)
    {
    }

    public static UserSession Start(Guid userId, TimeSpan idleTimeout, TimeSpan absoluteLifetime, string? ipAddress, string? userAgent, DateTime utcNow)
    {
        if (idleTimeout <= TimeSpan.Zero || absoluteLifetime < idleTimeout)
        {
            throw new DomainValidationException("Session lifetimes are inconsistent.", nameof(ExpiresAt));
        }

        return new UserSession(utcNow)
        {
            UserId = Check.NotEmpty(userId, nameof(UserId)),
            LastSeenAt = utcNow,
            ExpiresAt = utcNow.Add(idleTimeout),
            AbsoluteExpiresAt = utcNow.Add(absoluteLifetime),
            IpAddress = Check.Optional(ipAddress, nameof(IpAddress), 64),
            UserAgent = Check.Optional(userAgent, nameof(UserAgent), 512)
        };
    }

    public bool IsActive(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;

    public void Refresh(TimeSpan idleTimeout, DateTime utcNow)
    {
        if (!IsActive(utcNow))
        {
            throw new InvalidDomainOperationException("An inactive session cannot be refreshed.");
        }

        LastSeenAt = utcNow;
        var sliding = utcNow.Add(idleTimeout);
        ExpiresAt = sliding < AbsoluteExpiresAt ? sliding : AbsoluteExpiresAt;
    }

    public void Revoke(string reason, DateTime utcNow)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = utcNow;
        RevocationReason = Check.Required(reason, nameof(RevocationReason), 100);
        Touch(utcNow);
    }
}
