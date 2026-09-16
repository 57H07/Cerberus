using Cerberus.Domain.Applications;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Users;

namespace Cerberus.Domain.Organizations;

public enum TenantAccessFailure
{
    None = 0,
    OrganizationNotFound,
    OrganizationSuspended,
    NotAMember,
    MembershipInactive,
    UserDisabled,
    ClientDisabled,
    ApplicationNotAvailableForOrganization
}

/// <summary>
/// Server-side validation of the organization context requested by a client application.
/// The requested organization is only an intention: every rule below must pass before any
/// organization claim can be issued.
/// </summary>
public static class TenantAccessRules
{
    public static TenantAccessFailure Evaluate(
        User user,
        Organization? organization,
        OrganizationMembership? membership,
        OidcClient client,
        ClientApplication application)
    {
        if (!user.IsActive)
        {
            return TenantAccessFailure.UserDisabled;
        }

        if (!client.IsActive || client.ClientApplicationId != application.Id)
        {
            return TenantAccessFailure.ClientDisabled;
        }

        if (organization is null)
        {
            return TenantAccessFailure.OrganizationNotFound;
        }

        if (!organization.IsActive)
        {
            return TenantAccessFailure.OrganizationSuspended;
        }

        if (membership is null || membership.UserId != user.Id || membership.OrganizationId != organization.Id)
        {
            return TenantAccessFailure.NotAMember;
        }

        if (!membership.IsActive)
        {
            return TenantAccessFailure.MembershipInactive;
        }

        return application.IsAvailableTo(organization.Id)
            ? TenantAccessFailure.None
            : TenantAccessFailure.ApplicationNotAvailableForOrganization;
    }
}
