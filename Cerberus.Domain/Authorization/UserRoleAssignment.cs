using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;
using Cerberus.Domain.Organizations;

namespace Cerberus.Domain.Authorization;

/// <summary>
/// Assignment of a role to a user. The factories make incoherent assignments impossible:
/// - platform role: no organization;
/// - organization role: requires an active membership of the same user in the role's organization,
///   and the assignment carries that organization id (unique per user, role).
/// </summary>
public sealed class UserRoleAssignment : Entity
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public Guid? AssignedByUserId { get; private set; }

    private UserRoleAssignment()
    {
    }

    private UserRoleAssignment(DateTime utcNow) : base(utcNow)
    {
    }

    public static UserRoleAssignment ForPlatform(Guid userId, Role role, Guid? assignedByUserId, DateTime utcNow)
    {
        if (role.Scope != RoleScope.Platform)
        {
            throw new InvalidDomainOperationException("Only platform roles can be assigned without an organization.");
        }

        return new UserRoleAssignment(utcNow)
        {
            UserId = Check.NotEmpty(userId, nameof(UserId)),
            RoleId = role.Id,
            OrganizationId = null,
            AssignedByUserId = assignedByUserId
        };
    }

    public static UserRoleAssignment ForOrganization(OrganizationMembership membership, Role role, Guid? assignedByUserId, DateTime utcNow)
    {
        if (role.Scope != RoleScope.Organization)
        {
            throw new InvalidDomainOperationException("Platform roles cannot be assigned within an organization.");
        }

        if (role.OrganizationId != membership.OrganizationId)
        {
            throw new InvalidDomainOperationException("The role does not belong to the member's organization.");
        }

        if (!membership.IsActive)
        {
            throw new InvalidDomainOperationException("Roles can only be assigned to active members.");
        }

        return new UserRoleAssignment(utcNow)
        {
            UserId = membership.UserId,
            RoleId = role.Id,
            OrganizationId = membership.OrganizationId,
            AssignedByUserId = assignedByUserId
        };
    }
}
