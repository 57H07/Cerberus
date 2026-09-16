using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Applications;

public enum ClientApplicationStatus
{
    Active = 0,
    Disabled = 1
}

/// <summary>
/// Business application relying on Cerberus. Named <c>ClientApplication</c> to avoid clashing with the
/// <c>Cerberus.Application</c> layer namespace.
/// Invariants: always owned by exactly one organization; the owner organization always has an access entry
/// that cannot be removed; an organization can only use the application through an enabled access entry.
/// </summary>
public sealed class ClientApplication : Entity
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 1000;

    private readonly List<ApplicationOrganizationAccess> _organizationAccesses = [];

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid OwnerOrganizationId { get; private set; }
    public ClientApplicationStatus Status { get; private set; }

    public IReadOnlyCollection<ApplicationOrganizationAccess> OrganizationAccesses => _organizationAccesses.AsReadOnly();

    public bool IsActive => Status == ClientApplicationStatus.Active;

    private ClientApplication()
    {
    }

    private ClientApplication(DateTime utcNow) : base(utcNow)
    {
    }

    public static ClientApplication Create(string name, string? description, Guid ownerOrganizationId, DateTime utcNow)
    {
        var application = new ClientApplication(utcNow)
        {
            Name = Check.Required(name, nameof(Name), NameMaxLength),
            Description = Check.Optional(description, nameof(Description), DescriptionMaxLength),
            OwnerOrganizationId = Check.NotEmpty(ownerOrganizationId, nameof(OwnerOrganizationId)),
            Status = ClientApplicationStatus.Active
        };
        application._organizationAccesses.Add(ApplicationOrganizationAccess.Create(application.Id, ownerOrganizationId, utcNow));
        return application;
    }

    public void Update(string name, string? description, DateTime utcNow)
    {
        Name = Check.Required(name, nameof(Name), NameMaxLength);
        Description = Check.Optional(description, nameof(Description), DescriptionMaxLength);
        Touch(utcNow);
    }

    public void Disable(DateTime utcNow)
    {
        Status = ClientApplicationStatus.Disabled;
        Touch(utcNow);
    }

    public void Enable(DateTime utcNow)
    {
        Status = ClientApplicationStatus.Active;
        Touch(utcNow);
    }

    public ApplicationOrganizationAccess GrantOrganization(Guid organizationId, DateTime utcNow)
    {
        var existing = _organizationAccesses.FirstOrDefault(a => a.OrganizationId == organizationId);
        if (existing is not null)
        {
            existing.Resume(utcNow);
            return existing;
        }

        var access = ApplicationOrganizationAccess.Create(Id, organizationId, utcNow);
        _organizationAccesses.Add(access);
        Touch(utcNow);
        return access;
    }

    public void RemoveOrganization(Guid organizationId, DateTime utcNow)
    {
        if (organizationId == OwnerOrganizationId)
        {
            throw new InvalidDomainOperationException("The owner organization access cannot be removed.");
        }

        _organizationAccesses.RemoveAll(a => a.OrganizationId == organizationId);
        Touch(utcNow);
    }

    public void SetOrganizationAccessEnabled(Guid organizationId, bool enabled, DateTime utcNow)
    {
        var access = _organizationAccesses.FirstOrDefault(a => a.OrganizationId == organizationId)
            ?? throw new InvalidDomainOperationException("The organization has no access to this application.");

        if (enabled)
        {
            access.Resume(utcNow);
        }
        else
        {
            access.Suspend(utcNow);
        }
    }

    public bool IsAvailableTo(Guid organizationId)
    {
        return IsActive && _organizationAccesses.Any(a => a.OrganizationId == organizationId && a.IsEnabled);
    }
}
