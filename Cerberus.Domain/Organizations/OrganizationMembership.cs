using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Organizations;

/// <summary>
/// Link between a global user and an organization. Invariants: unique per (user, organization);
/// a revoked membership is terminal (a new invitation reactivates it explicitly through <see cref="Reactivate"/>).
/// </summary>
public sealed class OrganizationMembership : Entity
{
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public MembershipStatus Status { get; private set; }

    public bool IsActive => Status == MembershipStatus.Active;

    private OrganizationMembership()
    {
    }

    private OrganizationMembership(DateTime utcNow) : base(utcNow)
    {
    }

    public static OrganizationMembership Create(Guid organizationId, Guid userId, DateTime utcNow)
    {
        return new OrganizationMembership(utcNow)
        {
            OrganizationId = Check.NotEmpty(organizationId, nameof(OrganizationId)),
            UserId = Check.NotEmpty(userId, nameof(UserId)),
            Status = MembershipStatus.Active
        };
    }

    public void Suspend(DateTime utcNow)
    {
        if (Status == MembershipStatus.Revoked)
        {
            throw new InvalidDomainOperationException("A revoked membership cannot be suspended.");
        }

        Status = MembershipStatus.Suspended;
        Touch(utcNow);
    }

    public void Resume(DateTime utcNow)
    {
        if (Status == MembershipStatus.Revoked)
        {
            throw new InvalidDomainOperationException("A revoked membership cannot be resumed.");
        }

        Status = MembershipStatus.Active;
        Touch(utcNow);
    }

    public void Revoke(DateTime utcNow)
    {
        Status = MembershipStatus.Revoked;
        Touch(utcNow);
    }

    public void Reactivate(DateTime utcNow)
    {
        Status = MembershipStatus.Active;
        Touch(utcNow);
    }
}
