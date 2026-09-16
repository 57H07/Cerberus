using Cerberus.Domain.Authorization;

namespace Cerberus.Application.Authorization;

/// <summary>
/// Anti privilege-escalation rules for role management:
/// - an actor can only grant, create or edit a role whose permissions are all held by the actor in the same scope
///   (same organization for organization roles);
/// - an actor cannot change his own role assignments (prevents self-promotion and accidental self-lockout).
/// </summary>
public static class PrivilegeEscalationPolicy
{
    public static bool CanGrant(IReadOnlySet<string> actorPermissions, IEnumerable<string> rolePermissions)
        => rolePermissions.All(actorPermissions.Contains);

    public static bool CanGrant(IReadOnlySet<string> actorPermissions, Role role) => CanGrant(actorPermissions, role.PermissionCodes);

    public static bool CanManageAssignmentsOf(Guid actorUserId, Guid targetUserId) => actorUserId != targetUserId;
}
