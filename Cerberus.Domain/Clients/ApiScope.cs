using System.Text.RegularExpressions;
using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Clients;

/// <summary>
/// Scope protecting an API. Requesting it adds <see cref="Resource"/> to the access token audience.
/// Invariants: unique name that does not collide with standard scopes; resource is a valid audience identifier.
/// </summary>
public sealed partial class ApiScope : Entity
{
    public string Name { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Resource { get; private set; } = string.Empty;

    private ApiScope()
    {
    }

    private ApiScope(DateTime utcNow) : base(utcNow)
    {
    }

    public static ApiScope Create(string name, string displayName, string? description, string resource, DateTime utcNow)
    {
        if (!Scopes.IsValidName(name) || Scopes.Standard.Contains(name))
        {
            throw new DomainValidationException($"'{name}' is not a valid API scope name.", nameof(Name));
        }

        if (resource is null || !ResourcePattern().IsMatch(resource))
        {
            throw new DomainValidationException("Resource must be 3-100 characters: letters, digits, '.', '_', ':' or '-'.", nameof(Resource));
        }

        return new ApiScope(utcNow)
        {
            Name = name,
            DisplayName = Check.Required(displayName, nameof(DisplayName), 200),
            Description = Check.Optional(description, nameof(Description), 1000),
            Resource = resource
        };
    }

    public void Update(string displayName, string? description, DateTime utcNow)
    {
        DisplayName = Check.Required(displayName, nameof(DisplayName), 200);
        Description = Check.Optional(description, nameof(Description), 1000);
        Touch(utcNow);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9._:-]{3,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourcePattern();
}
