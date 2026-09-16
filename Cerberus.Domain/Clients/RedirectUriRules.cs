using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Clients;

/// <summary>
/// Registration rules for redirect URIs. Runtime matching is an exact, ordinal string comparison
/// performed by the OIDC library; wildcards are never allowed.
/// </summary>
public static class RedirectUriRules
{
    public const int MaxLength = 2000;

    public static string Validate(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            throw new DomainValidationException("Redirect URI is required and must not exceed 2000 characters.", fieldName);
        }

        value = value.Trim();

        if (value.Contains('*'))
        {
            throw new DomainValidationException("Wildcards are not allowed in redirect URIs.", fieldName);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new DomainValidationException($"'{value}' is not an absolute URI.", fieldName);
        }

        if (value.Contains('#'))
        {
            throw new DomainValidationException("Redirect URIs must not contain a fragment.", fieldName);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new DomainValidationException("Redirect URIs must not contain user information.", fieldName);
        }

        var isHttps = uri.Scheme == Uri.UriSchemeHttps;
        var isLoopbackHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
        {
            throw new DomainValidationException("Redirect URIs must use HTTPS (HTTP is only accepted for loopback hosts).", fieldName);
        }

        return value;
    }
}
