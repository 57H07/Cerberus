namespace Cerberus.Domain.Clients;

public enum ClientType
{
    /// <summary>Cannot keep a secret (SPA, native). Authenticates with PKCE only.</summary>
    Public = 0,

    /// <summary>Server-side web application holding a client secret. PKCE is still required.</summary>
    Confidential = 1
}

public enum ConsentPolicy
{
    /// <summary>The consent screen is displayed on every authorization request.</summary>
    AlwaysPrompt = 0,

    /// <summary>
    /// Consent is remembered per (user, client, organization). It is asked again when new scopes are requested,
    /// after a revocation, or after expiration when the client defines a consent lifetime
    /// (no lifetime means consent is asked at the first authorization only).
    /// </summary>
    Remembered = 1,

    /// <summary>First-party client flagged as trusted by a platform administrator: no consent screen.</summary>
    Trusted = 2
}

public enum ClientStatus
{
    Active = 0,
    Disabled = 1
}
