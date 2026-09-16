using Cerberus.Application.Authorization;
using Cerberus.Application.Common;
using Cerberus.Application.DTOs;
using Cerberus.Application.Exceptions;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Security;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Domain.Applications;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Exceptions;
using Cerberus.Domain.Organizations;

namespace Cerberus.Application.Services;

/// <summary>Platform administration of client applications, their OIDC clients and API scopes.</summary>
public interface IClientAdministrationService
{
    Task<IReadOnlyList<ApplicationDto>> ListApplicationsAsync(CancellationToken cancellationToken = default);
    Task<ApplicationDto> GetApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default);
    Task<Guid> CreateApplicationAsync(SaveApplicationDto dto, CancellationToken cancellationToken = default);
    Task UpdateApplicationAsync(Guid applicationId, SaveApplicationDto dto, CancellationToken cancellationToken = default);
    Task SetApplicationActiveAsync(Guid applicationId, bool active, CancellationToken cancellationToken = default);
    Task GrantOrganizationAsync(Guid applicationId, string organizationSlug, CancellationToken cancellationToken = default);
    Task RemoveOrganizationAsync(Guid applicationId, Guid organizationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OidcClientDto>> ListClientsAsync(Guid applicationId, CancellationToken cancellationToken = default);
    Task<OidcClientDto> GetClientAsync(Guid clientId, CancellationToken cancellationToken = default);
    Task<ClientSecretDto> CreateClientAsync(CreateOidcClientDto dto, CancellationToken cancellationToken = default);
    Task UpdateClientAsync(Guid clientId, UpdateOidcClientDto dto, CancellationToken cancellationToken = default);
    Task<ClientSecretDto> RotateSecretAsync(Guid clientId, CancellationToken cancellationToken = default);
    Task SetClientActiveAsync(Guid clientId, bool active, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApiScopeDto>> ListScopesAsync(CancellationToken cancellationToken = default);
    Task<Guid> CreateScopeAsync(SaveApiScopeDto dto, CancellationToken cancellationToken = default);
    Task UpdateScopeAsync(Guid scopeId, SaveApiScopeDto dto, CancellationToken cancellationToken = default);

    /// <summary>Scopes a client may be allowed to request: standard scopes plus registered API scopes.</summary>
    Task<IReadOnlyList<string>> ListAssignableScopeNamesAsync(CancellationToken cancellationToken = default);
}

public class ClientAdministrationService(
    IAccessGuard guard,
    IClientApplicationRepository applicationRepository,
    IOidcClientRepository clientRepository,
    IApiScopeRepository scopeRepository,
    IOrganizationRepository organizationRepository,
    IOidcClientRegistry registry,
    ITokenRevocationService tokenRevocation,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock) : IClientAdministrationService
{
    public async Task<IReadOnlyList<ApplicationDto>> ListApplicationsAsync(CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        var applications = await applicationRepository.ListAsync(cancellationToken);
        var organizations = await organizationRepository.GetByIdsAsync(
            applications.SelectMany(a => a.OrganizationAccesses.Select(x => x.OrganizationId)), cancellationToken);
        return applications.Select(a => ToDto(a, organizations)).ToList();
    }

    public async Task<ApplicationDto> GetApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        var application = await GetApplicationEntityAsync(applicationId, cancellationToken);
        var organizations = await organizationRepository.GetByIdsAsync(application.OrganizationAccesses.Select(x => x.OrganizationId), cancellationToken);
        return ToDto(application, organizations);
    }

    public async Task<Guid> CreateApplicationAsync(SaveApplicationDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        _ = await organizationRepository.GetByIdAsync(dto.OwnerOrganizationId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Organization), dto.OwnerOrganizationId);

        var application = ClientApplication.Create(dto.Name, dto.Description, dto.OwnerOrganizationId, clock.UtcNow);
        applicationRepository.Add(application);
        audit.Write(AuditActions.ApplicationCreated, AuditOutcome.Success, dto.OwnerOrganizationId, targetType: nameof(ClientApplication), targetId: application.Id,
            details: new { application.Name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return application.Id;
    }

    public async Task UpdateApplicationAsync(Guid applicationId, SaveApplicationDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        var application = await GetApplicationEntityAsync(applicationId, cancellationToken);
        application.Update(dto.Name, dto.Description, clock.UtcNow);
        audit.Write(AuditActions.ApplicationUpdated, targetType: nameof(ClientApplication), targetId: application.Id, details: new { application.Name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SetApplicationActiveAsync(Guid applicationId, bool active, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        var application = await GetApplicationEntityAsync(applicationId, cancellationToken);
        if (active)
        {
            application.Enable(clock.UtcNow);
        }
        else
        {
            application.Disable(clock.UtcNow);
        }

        audit.Write(AuditActions.ApplicationUpdated, targetType: nameof(ClientApplication), targetId: application.Id, details: new { active });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!active)
        {
            foreach (var client in await clientRepository.ListByApplicationAsync(application.Id, cancellationToken))
            {
                await tokenRevocation.RevokeClientAsync(client.ClientId, cancellationToken);
            }
        }
    }

    public async Task GrantOrganizationAsync(Guid applicationId, string organizationSlug, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        var application = await GetApplicationEntityAsync(applicationId, cancellationToken);
        var organization = await organizationRepository.GetBySlugAsync(organizationSlug?.Trim() ?? string.Empty, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Organization), organizationSlug ?? string.Empty);

        application.GrantOrganization(organization.Id, clock.UtcNow);
        audit.Write(AuditActions.ApplicationOrganizationAccessChanged, AuditOutcome.Success, organization.Id, nameof(ClientApplication), application.Id, new { granted = true });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveOrganizationAsync(Guid applicationId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        var application = await GetApplicationEntityAsync(applicationId, cancellationToken);
        application.RemoveOrganization(organizationId, clock.UtcNow);
        audit.Write(AuditActions.ApplicationOrganizationAccessChanged, AuditOutcome.Success, organizationId, nameof(ClientApplication), application.Id, new { granted = false });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OidcClientDto>> ListClientsAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        return (await clientRepository.ListByApplicationAsync(applicationId, cancellationToken)).Select(ToDto).ToList();
    }

    public async Task<OidcClientDto> GetClientAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        return ToDto(await GetClientEntityAsync(clientId, cancellationToken));
    }

    public async Task<ClientSecretDto> CreateClientAsync(CreateOidcClientDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        var application = await GetApplicationEntityAsync(dto.ApplicationId, cancellationToken);
        await EnsureScopesExistAsync(dto.AllowedScopes, cancellationToken);
        if (await clientRepository.GetByClientIdAsync(dto.ClientId, cancellationToken) is not null)
        {
            throw new DuplicateEntityException("client", "client ID");
        }

        var now = clock.UtcNow;
        var client = OidcClient.Create(application.Id, dto.ClientId, dto.DisplayName, dto.ClientType, dto.RedirectUris, dto.PostLogoutRedirectUris,
            dto.AllowedScopes, now);
        client.SetConsentPolicy(dto.ConsentPolicy, ToLifetime(dto.ConsentLifetimeDays), now);
        var secret = client.ClientType == ClientType.Confidential ? SecureTokens.Generate() : null;

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            clientRepository.Add(client);
            audit.Write(AuditActions.ClientCreated, AuditOutcome.Success, application.OwnerOrganizationId, nameof(OidcClient), client.ClientId,
                new { client.ClientType, client.ConsentPolicy, client.RedirectUris, client.AllowedScopes });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await registry.CreateAsync(client, secret, cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        return new ClientSecretDto(client.ClientId, secret);
    }

    public async Task UpdateClientAsync(Guid clientId, UpdateOidcClientDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        var client = await GetClientEntityAsync(clientId, cancellationToken);
        await EnsureScopesExistAsync(dto.AllowedScopes, cancellationToken);

        var now = clock.UtcNow;
        client.Rename(dto.DisplayName, now);
        client.UpdateRedirectUris(dto.RedirectUris, dto.PostLogoutRedirectUris, now);
        client.UpdateScopes(dto.AllowedScopes, now);
        client.SetConsentPolicy(dto.ConsentPolicy, ToLifetime(dto.ConsentLifetimeDays), now);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            audit.Write(AuditActions.ClientUpdated, targetType: nameof(OidcClient), targetId: client.ClientId,
                details: new { client.ConsentPolicy, client.RedirectUris, client.PostLogoutRedirectUris, client.AllowedScopes });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await registry.UpdateAsync(client, cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ClientSecretDto> RotateSecretAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        var client = await GetClientEntityAsync(clientId, cancellationToken);
        if (client.ClientType != ClientType.Confidential)
        {
            throw new InvalidDomainOperationException("Public clients have no secret.");
        }

        var secret = SecureTokens.Generate();
        await registry.SetSecretAsync(client, secret, cancellationToken);
        audit.Write(AuditActions.ClientSecretRotated, targetType: nameof(OidcClient), targetId: client.ClientId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ClientSecretDto(client.ClientId, secret);
    }

    public async Task SetClientActiveAsync(Guid clientId, bool active, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        var client = await GetClientEntityAsync(clientId, cancellationToken);
        if (active)
        {
            client.Enable(clock.UtcNow);
        }
        else
        {
            client.Disable(clock.UtcNow);
        }

        audit.Write(active ? AuditActions.ClientEnabled : AuditActions.ClientDisabled, targetType: nameof(OidcClient), targetId: client.ClientId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await registry.UpdateAsync(client, cancellationToken);
        if (!active)
        {
            await tokenRevocation.RevokeClientAsync(client.ClientId, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ApiScopeDto>> ListScopesAsync(CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformScopesManage, cancellationToken);
        return (await scopeRepository.ListAsync(cancellationToken)).Select(ToDto).ToList();
    }

    public async Task<Guid> CreateScopeAsync(SaveApiScopeDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformScopesManage, cancellationToken);
        var scope = ApiScope.Create(dto.Name, dto.DisplayName, dto.Description, dto.Resource, clock.UtcNow);
        if ((await scopeRepository.GetByNamesAsync([scope.Name], cancellationToken)).Count > 0)
        {
            throw new DuplicateEntityException("scope", "name");
        }

        scopeRepository.Add(scope);
        audit.Write(AuditActions.ScopeCreated, targetType: nameof(ApiScope), targetId: scope.Name, details: new { scope.Resource });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await registry.SyncScopeAsync(scope, cancellationToken);
        return scope.Id;
    }

    public async Task UpdateScopeAsync(Guid scopeId, SaveApiScopeDto dto, CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformScopesManage, cancellationToken);
        var scope = await scopeRepository.GetByIdAsync(scopeId, cancellationToken) ?? throw new EntityNotFoundException(nameof(ApiScope), scopeId);
        scope.Update(dto.DisplayName, dto.Description, clock.UtcNow);
        audit.Write(AuditActions.ScopeUpdated, targetType: nameof(ApiScope), targetId: scope.Name);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await registry.SyncScopeAsync(scope, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListAssignableScopeNamesAsync(CancellationToken cancellationToken = default)
    {
        await guard.RequirePlatformPermissionAsync(PermissionCatalog.PlatformClientsManage, cancellationToken);
        var apiScopes = await scopeRepository.ListAsync(cancellationToken);
        return Scopes.Standard.Concat(apiScopes.Select(s => s.Name)).ToList();
    }

    private async Task EnsureScopesExistAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        var apiScopeNames = scopes.Where(s => !Scopes.Standard.Contains(s)).Distinct().ToList();
        if (apiScopeNames.Count == 0)
        {
            return;
        }

        var existing = await scopeRepository.GetByNamesAsync(apiScopeNames, cancellationToken);
        var missing = apiScopeNames.Except(existing.Select(s => s.Name)).FirstOrDefault();
        if (missing is not null)
        {
            throw new DomainValidationException($"Scope '{missing}' is not registered.", "AllowedScopes");
        }
    }

    private async Task<ClientApplication> GetApplicationEntityAsync(Guid applicationId, CancellationToken cancellationToken)
        => await applicationRepository.GetByIdAsync(applicationId, cancellationToken) ?? throw new EntityNotFoundException(nameof(ClientApplication), applicationId);

    private async Task<OidcClient> GetClientEntityAsync(Guid clientId, CancellationToken cancellationToken)
        => await clientRepository.GetByIdAsync(clientId, cancellationToken) ?? throw new EntityNotFoundException(nameof(OidcClient), clientId);

    private static TimeSpan? ToLifetime(int? days) => days is > 0 ? TimeSpan.FromDays(days.Value) : null;

    private static ApplicationDto ToDto(ClientApplication a, IReadOnlyList<Organization> organizations)
        => new(a.Id, a.Name, a.Description, a.OwnerOrganizationId, a.IsActive,
            a.OrganizationAccesses
                .Select(x => new ApplicationAccessDto(
                    x.OrganizationId,
                    organizations.FirstOrDefault(o => o.Id == x.OrganizationId)?.Name ?? x.OrganizationId.ToString(),
                    x.IsEnabled,
                    x.OrganizationId == a.OwnerOrganizationId))
                .OrderByDescending(x => x.IsOwner).ThenBy(x => x.OrganizationName)
                .ToList());

    private static OidcClientDto ToDto(OidcClient c)
        => new(c.Id, c.ClientApplicationId, c.ClientId, c.DisplayName, c.ClientType, c.ConsentPolicy, c.ConsentLifetime, c.IsActive,
            c.RedirectUris, c.PostLogoutRedirectUris, c.AllowedScopes);

    private static ApiScopeDto ToDto(ApiScope s) => new(s.Id, s.Name, s.DisplayName, s.Description, s.Resource);
}
