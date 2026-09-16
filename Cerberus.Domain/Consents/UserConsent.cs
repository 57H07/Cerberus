using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Consents;

/// <summary>
/// Consent given by a user to a client, for one organization context.
/// Scoping consent to the organization means that consenting for organization A never releases the
/// organization B context (org id, org roles) to the same client.
/// Invariants: one row per (user, client, organization); granted scopes are never empty and always contain <c>openid</c>;
/// a revoked or expired consent covers nothing.
/// </summary>
public sealed class UserConsent : Entity
{
    public Guid UserId { get; private set; }
    public Guid OidcClientId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public List<string> GrantedScopes { get; private set; } = [];
    public DateTime GrantedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    /// <summary>Identifier of the permanent authorization created in the OIDC library; revoking it revokes derived tokens.</summary>
    public string? AuthorizationId { get; private set; }

    private UserConsent()
    {
    }

    private UserConsent(DateTime utcNow) : base(utcNow)
    {
    }

    public static UserConsent Grant(Guid userId, Guid oidcClientId, Guid organizationId, IEnumerable<string> scopes, TimeSpan? lifetime, DateTime utcNow)
    {
        var consent = new UserConsent(utcNow)
        {
            UserId = Check.NotEmpty(userId, nameof(UserId)),
            OidcClientId = Check.NotEmpty(oidcClientId, nameof(OidcClientId)),
            OrganizationId = Check.NotEmpty(organizationId, nameof(OrganizationId))
        };
        consent.Apply(scopes, lifetime, utcNow, merge: false);
        return consent;
    }

    public bool IsActive(DateTime utcNow) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > utcNow);

    public bool Covers(IEnumerable<string> requestedScopes, DateTime utcNow)
    {
        return IsActive(utcNow) && requestedScopes.All(GrantedScopes.Contains);
    }

    /// <summary>Extends an active consent with new scopes, or restarts a revoked/expired one with the requested scopes only.</summary>
    public void Renew(IEnumerable<string> scopes, TimeSpan? lifetime, DateTime utcNow)
    {
        var merge = IsActive(utcNow);
        RevokedAt = null;
        Apply(scopes, lifetime, utcNow, merge);
        Touch(utcNow);
    }

    public void LinkAuthorization(string authorizationId)
    {
        AuthorizationId = Check.Required(authorizationId, nameof(AuthorizationId), 100);
    }

    public void Revoke(DateTime utcNow)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = utcNow;
        AuthorizationId = null;
        Touch(utcNow);
    }

    private void Apply(IEnumerable<string> scopes, TimeSpan? lifetime, DateTime utcNow, bool merge)
    {
        var set = new List<string>(merge ? GrantedScopes : []);
        foreach (var scope in scopes.Where(s => !set.Contains(s)))
        {
            set.Add(scope);
        }

        if (!set.Contains(Clients.Scopes.OpenId))
        {
            throw new DomainValidationException("A consent must include the openid scope.", nameof(GrantedScopes));
        }

        GrantedScopes = set;
        GrantedAt = utcNow;
        ExpiresAt = lifetime is null ? null : utcNow.Add(lifetime.Value);
    }
}
