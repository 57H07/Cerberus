using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;
using Cerberus.Domain.Users;

namespace Cerberus.Domain.Organizations;

/// <summary>
/// Invitation to join an organization. Only the SHA-256 hash of the token is stored.
/// Invariants: single use, time limited, can only be accepted by the user owning the invited email.
/// </summary>
public sealed class Invitation : Entity
{
    public Guid OrganizationId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public Guid InvitedByUserId { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    private Invitation()
    {
    }

    private Invitation(DateTime utcNow) : base(utcNow)
    {
    }

    public static Invitation Create(Guid organizationId, string email, string tokenHash, Guid invitedByUserId, TimeSpan lifetime, DateTime utcNow)
    {
        if (!User.IsValidEmail(email))
        {
            throw new DomainValidationException("Email address is invalid.", nameof(Email));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainValidationException("Invitation lifetime must be positive.", nameof(ExpiresAt));
        }

        return new Invitation(utcNow)
        {
            OrganizationId = Check.NotEmpty(organizationId, nameof(OrganizationId)),
            Email = email.Trim(),
            NormalizedEmail = IdentityNormalizer.Normalize(email),
            TokenHash = Check.Required(tokenHash, nameof(TokenHash), 128),
            InvitedByUserId = Check.NotEmpty(invitedByUserId, nameof(InvitedByUserId)),
            ExpiresAt = utcNow.Add(lifetime)
        };
    }

    public bool IsPending(DateTime utcNow) => AcceptedAt is null && RevokedAt is null && ExpiresAt > utcNow;

    public void Accept(User user, DateTime utcNow)
    {
        if (!IsPending(utcNow))
        {
            throw new InvalidDomainOperationException("The invitation is no longer valid.");
        }

        if (user.NormalizedEmail != NormalizedEmail)
        {
            throw new InvalidDomainOperationException("The invitation was issued for another email address.");
        }

        AcceptedAt = utcNow;
        AcceptedByUserId = user.Id;
        Touch(utcNow);
    }

    public void Revoke(DateTime utcNow)
    {
        if (AcceptedAt is not null)
        {
            throw new InvalidDomainOperationException("An accepted invitation cannot be revoked.");
        }

        RevokedAt ??= utcNow;
        Touch(utcNow);
    }
}
