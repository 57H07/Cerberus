using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Security;
using Cerberus.Application.Services;
using Cerberus.Domain.Applications;
using Cerberus.Domain.Authorization;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Cerberus.IntegrationTests.Infrastructure;

/// <summary>
/// Builds an isolated data set per test (unique slugs, logins and client ids) so tests can share one database.
/// </summary>
public sealed class TestScenario(CerberusWebFactory factory)
{
    public const string Password = "Correct-Horse-Battery-42";

    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public string ClientId => $"client-{_suffix}";
    public string ClientSecret { get; } = "secret-" + Guid.NewGuid().ToString("N");
    public string RedirectUri => $"https://app-{_suffix}.example.com/signin-oidc";
    public string PostLogoutRedirectUri => $"https://app-{_suffix}.example.com/signout-callback-oidc";
    public string ApiScope => $"api_{_suffix}";
    public string ApiResource => $"resource-{_suffix}";

    public string Name(string prefix) => $"{prefix}-{_suffix}";

    public Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
        => ExecuteAsync(action);

    public async Task<Organization> CreateOrganizationAsync(string prefix, bool withAdministratorRole = true)
    {
        return await ExecuteAsync(async sp =>
        {
            var now = DateTime.UtcNow;
            var organization = Organization.Create(prefix, Name(prefix), now);
            sp.GetRequiredService<IOrganizationRepository>().Add(organization);
            if (withAdministratorRole)
            {
                sp.GetRequiredService<IRoleRepository>().Add(PlatformAdministrationService.CreateOrganizationAdministratorRole(organization.Id, now));
            }

            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            return organization;
        });
    }

    public async Task<User> CreateUserAsync(string prefix)
    {
        return await ExecuteAsync(async sp =>
        {
            var login = Name(prefix);
            var user = User.Create(prefix, "Test", $"{login}@example.com", login, DateTime.UtcNow);
            user.ConfirmEmail(DateTime.UtcNow);
            var result = await sp.GetRequiredService<IIdentityService>().CreateUserAsync(user, Password);
            result.Succeeded.Should().BeTrue(string.Join(" ", result.Errors));
            return user;
        });
    }

    public async Task AddMemberAsync(Organization organization, User user, params string[] organizationRoleNames)
    {
        await ExecuteAsync(async sp =>
        {
            var membership = OrganizationMembership.Create(organization.Id, user.Id, DateTime.UtcNow);
            sp.GetRequiredService<IMembershipRepository>().Add(membership);
            foreach (var roleName in organizationRoleNames)
            {
                var role = await sp.GetRequiredService<IRoleRepository>().FindOrganizationRoleByNameAsync(organization.Id, roleName)
                    ?? throw new InvalidOperationException($"Role {roleName} not found.");
                sp.GetRequiredService<IRoleAssignmentRepository>().Add(UserRoleAssignment.ForOrganization(membership, role, null, DateTime.UtcNow));
            }

            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            return true;
        });
    }

    public async Task<Role> CreateOrganizationRoleAsync(Organization organization, string name, params string[] permissions)
    {
        return await ExecuteAsync(async sp =>
        {
            var role = Role.CreateOrganizationRole(organization.Id, name, null, false, DateTime.UtcNow);
            role.SetPermissions(permissions, DateTime.UtcNow);
            sp.GetRequiredService<IRoleRepository>().Add(role);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            return role;
        });
    }

    public async Task GrantPlatformAdministratorAsync(User user)
    {
        await ExecuteAsync(async sp =>
        {
            var role = await sp.GetRequiredService<IRoleRepository>().FindPlatformRoleByNameAsync(Role.PlatformAdministratorName);
            sp.GetRequiredService<IRoleAssignmentRepository>().Add(UserRoleAssignment.ForPlatform(user.Id, role!, null, DateTime.UtcNow));
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            return true;
        });
    }

    public async Task<(ClientApplication Application, OidcClient Client)> CreateClientAsync(
        Organization owner,
        ConsentPolicy consentPolicy = ConsentPolicy.Remembered,
        params Organization[] grantedOrganizations)
    {
        return await ExecuteAsync(async sp =>
        {
            var now = DateTime.UtcNow;
            var scope = Cerberus.Domain.Clients.ApiScope.Create(ApiScope, "Test API", null, ApiResource, now);
            sp.GetRequiredService<IApiScopeRepository>().Add(scope);

            var application = ClientApplication.Create(Name("app"), null, owner.Id, now);
            foreach (var organization in grantedOrganizations)
            {
                application.GrantOrganization(organization.Id, now);
            }

            sp.GetRequiredService<IClientApplicationRepository>().Add(application);
            var client = OidcClient.Create(application.Id, ClientId, "Test client", ClientType.Confidential, [RedirectUri], [PostLogoutRedirectUri],
                [Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess, Scopes.OrganizationRoles, ApiScope], now);
            client.SetConsentPolicy(consentPolicy, null, now);
            sp.GetRequiredService<IOidcClientRepository>().Add(client);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

            var registry = sp.GetRequiredService<IOidcClientRegistry>();
            await registry.SyncScopeAsync(scope);
            await registry.CreateAsync(client, ClientSecret);
            return (application, client);
        });
    }

    private async Task<T> ExecuteAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }
}
