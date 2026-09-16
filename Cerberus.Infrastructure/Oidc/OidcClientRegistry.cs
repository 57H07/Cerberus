using Cerberus.Application.Interfaces.Security;
using Cerberus.Domain.Clients;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Cerberus.Infrastructure.Oidc;

/// <summary>
/// Projects <see cref="OidcClient"/> (source of truth) into the OpenIddict application store.
/// Only the authorization code flow (+ refresh token when <c>offline_access</c> is allowed) is permitted,
/// PKCE is required for every client, and a disabled client loses every endpoint permission.
/// </summary>
public sealed class OidcClientRegistry(IOpenIddictApplicationManager applicationManager, IOpenIddictScopeManager scopeManager) : IOidcClientRegistry
{
    public async Task CreateAsync(OidcClient client, string? clientSecret, CancellationToken cancellationToken = default)
    {
        var descriptor = new OpenIddictApplicationDescriptor { ClientId = client.ClientId, ClientSecret = clientSecret };
        Apply(descriptor, client);
        await applicationManager.CreateAsync(descriptor, cancellationToken);
    }

    public async Task UpdateAsync(OidcClient client, CancellationToken cancellationToken = default)
    {
        var application = await applicationManager.FindByClientIdAsync(client.ClientId, cancellationToken)
            ?? throw new InvalidOperationException($"OIDC application '{client.ClientId}' is missing from the protocol store.");

        // Populating from the stored application keeps the hashed secret untouched.
        var descriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(descriptor, application, cancellationToken);
        Apply(descriptor, client);
        await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
    }

    public async Task SetSecretAsync(OidcClient client, string clientSecret, CancellationToken cancellationToken = default)
    {
        var application = await applicationManager.FindByClientIdAsync(client.ClientId, cancellationToken)
            ?? throw new InvalidOperationException($"OIDC application '{client.ClientId}' is missing from the protocol store.");
        await applicationManager.UpdateAsync(application, clientSecret, cancellationToken);
    }

    public async Task SyncScopeAsync(ApiScope scope, CancellationToken cancellationToken = default)
    {
        var existing = await scopeManager.FindByNameAsync(scope.Name, cancellationToken);
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = scope.Name,
            DisplayName = scope.DisplayName,
            Description = scope.Description,
            Resources = { scope.Resource }
        };

        if (existing is null)
        {
            await scopeManager.CreateAsync(descriptor, cancellationToken);
        }
        else
        {
            await scopeManager.UpdateAsync(existing, descriptor, cancellationToken);
        }
    }

    private static void Apply(OpenIddictApplicationDescriptor descriptor, OidcClient client)
    {
        descriptor.DisplayName = client.DisplayName;
        descriptor.ClientType = client.ClientType == ClientType.Confidential ? ClientTypes.Confidential : ClientTypes.Public;
        descriptor.ApplicationType = ApplicationTypes.Web;
        descriptor.ConsentType = ConsentTypes.Explicit;

        descriptor.RedirectUris.Clear();
        foreach (var uri in client.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        descriptor.PostLogoutRedirectUris.Clear();
        foreach (var uri in client.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        descriptor.Permissions.Clear();
        descriptor.Requirements.Clear();
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);

        if (!client.IsActive)
        {
            return;
        }

        descriptor.Permissions.UnionWith(
        [
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.Endpoints.EndSession,
            Permissions.Endpoints.Revocation,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.ResponseTypes.Code
        ]);

        if (client.AllowsRefreshTokens)
        {
            descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
        }

        foreach (var scope in client.AllowedScopes.Where(s => s != Domain.Clients.Scopes.OpenId))
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }
    }
}
