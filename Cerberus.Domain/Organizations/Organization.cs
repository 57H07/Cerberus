using System.Text.RegularExpressions;
using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Organizations;

/// <summary>
/// Logical tenant. Invariants: the slug is lower-case, URL safe, globally unique and immutable
/// (it is the value client applications send in the <c>organization</c> authorization parameter);
/// a suspended organization cannot be used to obtain tokens.
/// </summary>
public sealed partial class Organization : Entity
{
    public const int NameMaxLength = 200;

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public OrganizationStatus Status { get; private set; }

    public bool IsActive => Status == OrganizationStatus.Active;

    private Organization()
    {
    }

    private Organization(DateTime utcNow) : base(utcNow)
    {
    }

    public static Organization Create(string name, string slug, DateTime utcNow)
    {
        if (!IsValidSlug(slug))
        {
            throw new DomainValidationException(
                "Slug must be 3-63 characters: lower-case letters, digits and inner hyphens.", nameof(Slug));
        }

        return new Organization(utcNow)
        {
            Name = Check.Required(name, nameof(Name), NameMaxLength),
            Slug = slug,
            Status = OrganizationStatus.Active
        };
    }

    public void Rename(string name, DateTime utcNow)
    {
        Name = Check.Required(name, nameof(Name), NameMaxLength);
        Touch(utcNow);
    }

    public void Suspend(DateTime utcNow)
    {
        Status = OrganizationStatus.Suspended;
        Touch(utcNow);
    }

    public void Activate(DateTime utcNow)
    {
        Status = OrganizationStatus.Active;
        Touch(utcNow);
    }

    public static bool IsValidSlug(string? slug) => slug is not null && SlugPattern().IsMatch(slug);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
