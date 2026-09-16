using Cerberus.Domain.Clients;

namespace Cerberus.Application.Oidc;

[Flags]
public enum ClaimDestinations
{
    None = 0,
    IdentityToken = 1,
    AccessToken = 2
}

public static class CerberusClaimTypes
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string GivenName = "given_name";
    public const string FamilyName = "family_name";
    public const string PreferredUserName = "preferred_username";
    public const string Email = "email";
    public const string EmailVerified = "email_verified";
    public const string SessionId = "sid";
    public const string OrganizationId = "org_id";
    public const string OrganizationSlug = "org_slug";
    public const string OrganizationRoles = "org_roles";
}

public sealed record TokenClaim(string Type, string Value, ClaimDestinations Destinations);

/// <summary>
/// Validated, minimal identity to embed in tokens. Built only after the tenant context, client and consent
/// were validated server side; the protocol layer maps it to library-specific principals.
/// </summary>
public sealed record TokenSubject(
    Guid UserId,
    Guid OidcClientId,
    Guid OrganizationId,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Resources,
    IReadOnlyList<TokenClaim> Claims,
    string? AuthorizationId,
    bool IsRememberedConsent);

/// <summary>
/// Claim release policy. Principle of least privilege:
/// - access tokens only carry what an API needs to authorize: sub, org_id and (with <c>org.roles</c>) org_roles;
/// - profile and email claims go to the ID token only when the matching scope was granted;
/// - platform roles are never released to client applications.
/// </summary>
public static class ClaimsPolicy
{
    public static IReadOnlyList<TokenClaim> Build(
        Domain.Users.User user,
        Domain.Organizations.Organization organization,
        IReadOnlyCollection<string> grantedScopes,
        IReadOnlyCollection<string> organizationRoles,
        Guid? sessionId)
    {
        var claims = new List<TokenClaim>
        {
            new(CerberusClaimTypes.Subject, user.Id.ToString(), ClaimDestinations.IdentityToken | ClaimDestinations.AccessToken),
            new(CerberusClaimTypes.OrganizationId, organization.Id.ToString(), ClaimDestinations.IdentityToken | ClaimDestinations.AccessToken),
            new(CerberusClaimTypes.OrganizationSlug, organization.Slug, ClaimDestinations.IdentityToken)
        };

        if (sessionId is not null)
        {
            claims.Add(new(CerberusClaimTypes.SessionId, sessionId.Value.ToString(), ClaimDestinations.IdentityToken));
        }

        if (grantedScopes.Contains(Scopes.Profile))
        {
            claims.Add(new(CerberusClaimTypes.Name, $"{user.FirstName} {user.LastName}", ClaimDestinations.IdentityToken));
            claims.Add(new(CerberusClaimTypes.GivenName, user.FirstName, ClaimDestinations.IdentityToken));
            claims.Add(new(CerberusClaimTypes.FamilyName, user.LastName, ClaimDestinations.IdentityToken));
            claims.Add(new(CerberusClaimTypes.PreferredUserName, user.UserName, ClaimDestinations.IdentityToken));
        }

        if (grantedScopes.Contains(Scopes.Email))
        {
            claims.Add(new(CerberusClaimTypes.Email, user.Email, ClaimDestinations.IdentityToken));
            claims.Add(new(CerberusClaimTypes.EmailVerified, user.EmailConfirmed ? "true" : "false", ClaimDestinations.IdentityToken));
        }

        if (grantedScopes.Contains(Scopes.OrganizationRoles))
        {
            claims.AddRange(organizationRoles.Distinct(StringComparer.Ordinal)
                .Select(role => new TokenClaim(CerberusClaimTypes.OrganizationRoles, role, ClaimDestinations.IdentityToken | ClaimDestinations.AccessToken)));
        }

        return claims;
    }
}
