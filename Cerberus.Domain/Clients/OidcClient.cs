using System.Text.RegularExpressions;
using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Clients;

/// <summary>
/// OIDC registration of a <see cref="Applications.ClientApplication"/>. This entity is the source of truth for the
/// client configuration; the infrastructure projects it into the OIDC library store (which also keeps the hashed secret).
/// Invariants:
/// - <c>client_id</c> is unique and immutable;
/// - at least one redirect URI, every URI satisfies <see cref="RedirectUriRules"/>;
/// - allowed scopes always contain <c>openid</c> and are valid scope names;
/// - Authorization Code + PKCE is the only interactive flow (no implicit, no password grant), for both client types;
/// - a consent lifetime is only meaningful with <see cref="ConsentPolicy.Remembered"/>.
/// </summary>
public sealed partial class OidcClient : Entity
{
    public const int ClientIdMaxLength = 100;
    public const int DisplayNameMaxLength = 200;

    public Guid ClientApplicationId { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public ClientType ClientType { get; private set; }
    public ConsentPolicy ConsentPolicy { get; private set; }
    public TimeSpan? ConsentLifetime { get; private set; }
    public ClientStatus Status { get; private set; }
    public List<string> RedirectUris { get; private set; } = [];
    public List<string> PostLogoutRedirectUris { get; private set; } = [];
    public List<string> AllowedScopes { get; private set; } = [];

    public bool IsActive => Status == ClientStatus.Active;

    public bool AllowsRefreshTokens => AllowedScopes.Contains(Scopes.OfflineAccess);

    private OidcClient()
    {
    }

    private OidcClient(DateTime utcNow) : base(utcNow)
    {
    }

    public static OidcClient Create(
        Guid clientApplicationId,
        string clientId,
        string displayName,
        ClientType clientType,
        IEnumerable<string> redirectUris,
        IEnumerable<string> postLogoutRedirectUris,
        IEnumerable<string> allowedScopes,
        DateTime utcNow)
    {
        if (!IsValidClientId(clientId))
        {
            throw new DomainValidationException(
                "Client ID must be 3-100 characters: letters, digits, '.', '_' or '-'.", nameof(ClientId));
        }

        var client = new OidcClient(utcNow)
        {
            ClientApplicationId = Check.NotEmpty(clientApplicationId, nameof(ClientApplicationId)),
            ClientId = clientId,
            DisplayName = Check.Required(displayName, nameof(DisplayName), DisplayNameMaxLength),
            ClientType = clientType,
            ConsentPolicy = ConsentPolicy.Remembered,
            Status = ClientStatus.Active
        };
        client.ApplyRedirectUris(redirectUris, postLogoutRedirectUris);
        client.ApplyScopes(allowedScopes);
        return client;
    }

    public void Rename(string displayName, DateTime utcNow)
    {
        DisplayName = Check.Required(displayName, nameof(DisplayName), DisplayNameMaxLength);
        Touch(utcNow);
    }

    public void UpdateRedirectUris(IEnumerable<string> redirectUris, IEnumerable<string> postLogoutRedirectUris, DateTime utcNow)
    {
        ApplyRedirectUris(redirectUris, postLogoutRedirectUris);
        Touch(utcNow);
    }

    public void UpdateScopes(IEnumerable<string> allowedScopes, DateTime utcNow)
    {
        ApplyScopes(allowedScopes);
        Touch(utcNow);
    }

    public void SetConsentPolicy(ConsentPolicy policy, TimeSpan? consentLifetime, DateTime utcNow)
    {
        if (consentLifetime is not null && (policy != ConsentPolicy.Remembered || consentLifetime <= TimeSpan.Zero))
        {
            throw new DomainValidationException(
                "A positive consent lifetime can only be set with the Remembered policy.", nameof(ConsentLifetime));
        }

        ConsentPolicy = policy;
        ConsentLifetime = consentLifetime;
        Touch(utcNow);
    }

    public void Disable(DateTime utcNow)
    {
        Status = ClientStatus.Disabled;
        Touch(utcNow);
    }

    public void Enable(DateTime utcNow)
    {
        Status = ClientStatus.Active;
        Touch(utcNow);
    }

    public bool AllowsScopes(IEnumerable<string> scopes) => scopes.All(AllowedScopes.Contains);

    public static bool IsValidClientId(string? clientId) => clientId is not null && ClientIdPattern().IsMatch(clientId);

    private void ApplyRedirectUris(IEnumerable<string> redirectUris, IEnumerable<string> postLogoutRedirectUris)
    {
        var redirects = redirectUris.Select(u => RedirectUriRules.Validate(u, nameof(RedirectUris))).Distinct(StringComparer.Ordinal).ToList();
        if (redirects.Count == 0)
        {
            throw new DomainValidationException("At least one redirect URI is required.", nameof(RedirectUris));
        }

        RedirectUris = redirects;
        PostLogoutRedirectUris = postLogoutRedirectUris
            .Select(u => RedirectUriRules.Validate(u, nameof(PostLogoutRedirectUris)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private void ApplyScopes(IEnumerable<string> allowedScopes)
    {
        var scopes = allowedScopes.Select(s => s.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var invalid = scopes.FirstOrDefault(s => !Scopes.IsValidName(s));
        if (invalid is not null)
        {
            throw new DomainValidationException($"'{invalid}' is not a valid scope name.", nameof(AllowedScopes));
        }

        if (!scopes.Contains(Scopes.OpenId))
        {
            scopes.Insert(0, Scopes.OpenId);
        }

        AllowedScopes = scopes;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9._-]{3,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdPattern();
}
