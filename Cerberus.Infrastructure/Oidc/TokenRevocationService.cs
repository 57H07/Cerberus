using Cerberus.Application.Interfaces.Security;
using Cerberus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace Cerberus.Infrastructure.Oidc;

public sealed class TokenRevocationService(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    CerberusDbContext context,
    ILogger<TokenRevocationService> logger) : ITokenRevocationService
{
    public async Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var subject = userId.ToString();
        var authorizations = await authorizationManager.RevokeBySubjectAsync(subject, cancellationToken);
        var tokens = await tokenManager.RevokeBySubjectAsync(subject, cancellationToken);
        logger.LogInformation("Revoked {Authorizations} authorizations and {Tokens} tokens for user {UserId}.", authorizations, tokens, userId);
    }

    public async Task RevokeUserInOrganizationAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        var subject = userId.ToString();
        var ids = await AuthorizationsForOrganization(organizationId)
            .Where(a => a.Subject == subject)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        await RevokeAuthorizationsAsync(ids, cancellationToken);
    }

    public async Task RevokeOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var ids = await AuthorizationsForOrganization(organizationId).Select(a => a.Id).ToListAsync(cancellationToken);
        await RevokeAuthorizationsAsync(ids, cancellationToken);
    }

    public async Task RevokeClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null)
        {
            return;
        }

        var id = await applicationManager.GetIdAsync(application, cancellationToken);
        var authorizations = await authorizationManager.RevokeByApplicationIdAsync(id!, cancellationToken);
        var tokens = await tokenManager.RevokeByApplicationIdAsync(id!, cancellationToken);
        logger.LogInformation("Revoked {Authorizations} authorizations and {Tokens} tokens for client {ClientId}.", authorizations, tokens, clientId);
    }

    public async Task RevokeAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await authorizationManager.FindByIdAsync(authorizationId, cancellationToken);
        if (authorization is not null)
        {
            await authorizationManager.TryRevokeAsync(authorization, cancellationToken);
        }

        await tokenManager.RevokeByAuthorizationIdAsync(authorizationId, cancellationToken);
    }

    private IQueryable<OpenIddictEntityFrameworkCoreAuthorization<Guid>> AuthorizationsForOrganization(Guid organizationId)
    {
        // Properties are stored as JSON; the organization id is a GUID so a textual match is unambiguous.
        var marker = $"\"{OidcAuthorizationProperties.OrganizationId}\":\"{organizationId}\"";
        return context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>()
            .AsNoTracking()
            .Where(a => a.Status == OpenIddictConstants.Statuses.Valid && a.Properties != null && a.Properties.Contains(marker));
    }

    private async Task RevokeAuthorizationsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids)
        {
            await RevokeAuthorizationAsync(id.ToString(), cancellationToken);
        }
    }
}
