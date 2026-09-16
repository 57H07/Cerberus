using Cerberus.Domain.Common;

namespace Cerberus.Domain.Applications;

/// <summary>
/// Grants an organization the right to use a client application. Created by a platform administrator;
/// the organization administrator may suspend or resume it for its own organization.
/// </summary>
public sealed class ApplicationOrganizationAccess : Entity
{
    public Guid ClientApplicationId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public bool IsEnabled { get; private set; }

    private ApplicationOrganizationAccess()
    {
    }

    private ApplicationOrganizationAccess(DateTime utcNow) : base(utcNow)
    {
    }

    internal static ApplicationOrganizationAccess Create(Guid clientApplicationId, Guid organizationId, DateTime utcNow)
    {
        return new ApplicationOrganizationAccess(utcNow)
        {
            ClientApplicationId = Check.NotEmpty(clientApplicationId, nameof(ClientApplicationId)),
            OrganizationId = Check.NotEmpty(organizationId, nameof(OrganizationId)),
            IsEnabled = true
        };
    }

    internal void Suspend(DateTime utcNow)
    {
        IsEnabled = false;
        Touch(utcNow);
    }

    internal void Resume(DateTime utcNow)
    {
        IsEnabled = true;
        Touch(utcNow);
    }
}
