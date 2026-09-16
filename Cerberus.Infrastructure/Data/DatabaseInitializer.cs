using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Security;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Application.Services;
using Cerberus.Domain.Applications;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Common;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;
using Cerberus.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cerberus.Infrastructure.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Applies pending migrations at startup. Intended for Development; production uses a migration bundle.</summary>
    public bool MigrateOnStartup { get; set; }
}

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string? AdminEmail { get; set; }
    public string? AdminUserName { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminFirstName { get; set; } = "Platform";
    public string AdminLastName { get; set; } = "Administrator";
}

public sealed class DemoSeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; }
    public string? DemoUserPassword { get; set; }
    public string? DemoClientSecret { get; set; }
    public string DemoClientBaseUrl { get; set; } = "https://localhost:5003";
}

/// <summary>
/// Startup initialization: migrations (optional), system data required in every environment
/// (platform administrator role, signing keys), secure bootstrap of the first administrator,
/// and demonstration data restricted to the Development environment.
/// </summary>
public sealed class DatabaseInitializer(
    CerberusDbContext context,
    IRoleRepository roleRepository,
    IRoleAssignmentRepository assignmentRepository,
    IUserRepository userRepository,
    IOrganizationRepository organizationRepository,
    IMembershipRepository membershipRepository,
    IClientApplicationRepository applicationRepository,
    IOidcClientRepository clientRepository,
    IApiScopeRepository scopeRepository,
    IIdentityService identityService,
    IOidcClientRegistry clientRegistry,
    SigningKeyManager keyManager,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    IHostEnvironment environment,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<BootstrapOptions> bootstrapOptions,
    IOptions<DemoSeedOptions> seedOptions,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (databaseOptions.Value.MigrateOnStartup)
        {
            logger.LogInformation("Applying database migrations.");
            await context.Database.MigrateAsync(cancellationToken);
        }

        var platformAdministrator = await EnsurePlatformAdministratorRoleAsync(cancellationToken);
        await keyManager.EnsureKeysAsync(cancellationToken);
        await BootstrapAdministratorAsync(platformAdministrator, cancellationToken);

        if (seedOptions.Value.Enabled)
        {
            if (!environment.IsDevelopment())
            {
                logger.LogWarning("Demo seed is only allowed in the Development environment; skipped.");
                return;
            }

            await SeedDemoDataAsync(cancellationToken);
        }
    }

    private async Task<Role> EnsurePlatformAdministratorRoleAsync(CancellationToken cancellationToken)
    {
        var role = await roleRepository.FindPlatformRoleByNameAsync(Role.PlatformAdministratorName, cancellationToken);
        var now = clock.UtcNow;
        if (role is null)
        {
            role = Role.CreatePlatformRole(Role.PlatformAdministratorName, "Full administration of the platform", true, now);
            roleRepository.Add(role);
        }

        // Re-synchronized at every start so new catalogue permissions reach the system role.
        var expected = PermissionCatalog.CodesFor(RoleScope.Platform).ToHashSet();
        if (!expected.SetEquals(role.PermissionCodes))
        {
            role.InitializeSystemPermissions(expected, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return role;
    }

    private async Task BootstrapAdministratorAsync(Role platformAdministrator, CancellationToken cancellationToken)
    {
        if (await assignmentRepository.CountUsersWithRoleAsync(platformAdministrator.Id, cancellationToken) > 0)
        {
            return;
        }

        var options = bootstrapOptions.Value;
        if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            logger.LogWarning("No platform administrator exists. Set Bootstrap:AdminEmail, Bootstrap:AdminUserName and Bootstrap:AdminPassword (environment or user secrets) to create one.");
            return;
        }

        var now = clock.UtcNow;
        var user = await userRepository.FindByNormalizedEmailAsync(IdentityNormalizer.Normalize(options.AdminEmail), cancellationToken);
        if (user is null)
        {
            user = User.Create(options.AdminFirstName, options.AdminLastName, options.AdminEmail, options.AdminUserName ?? "admin", now);
            user.ConfirmEmail(now);
            var result = await identityService.CreateUserAsync(user, options.AdminPassword, cancellationToken);
            if (!result.Succeeded)
            {
                logger.LogError("Bootstrap administrator could not be created: {Errors}", string.Join(" ", result.Errors));
                return;
            }
        }

        assignmentRepository.Add(UserRoleAssignment.ForPlatform(user.Id, platformAdministrator, null, now));
        audit.Write(AuditActions.BootstrapAdministratorCreated, targetType: nameof(User), targetId: user.Id, actorUserId: user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Bootstrap platform administrator {UserName} created. Remove the bootstrap password from the configuration.", user.UserName);
    }

    private async Task SeedDemoDataAsync(CancellationToken cancellationToken)
    {
        var options = seedOptions.Value;
        if (await organizationRepository.GetBySlugAsync("acme", cancellationToken) is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.DemoUserPassword) || string.IsNullOrWhiteSpace(options.DemoClientSecret))
        {
            logger.LogWarning("Demo seed skipped: set Seed:DemoUserPassword and Seed:DemoClientSecret.");
            return;
        }

        var now = clock.UtcNow;
        var acme = Organization.Create("Acme Corporation", "acme", now);
        var globex = Organization.Create("Globex", "globex", now);
        var initech = Organization.Create("Initech", "initech", now);
        organizationRepository.Add(acme);
        organizationRepository.Add(globex);
        organizationRepository.Add(initech);
        var acmeAdmin = PlatformAdministrationService.CreateOrganizationAdministratorRole(acme.Id, now);
        roleRepository.Add(acmeAdmin);
        roleRepository.Add(PlatformAdministrationService.CreateOrganizationAdministratorRole(globex.Id, now));
        roleRepository.Add(PlatformAdministrationService.CreateOrganizationAdministratorRole(initech.Id, now));
        var acmeSales = Role.CreateOrganizationRole(acme.Id, "Sales", "Sales team", false, now);
        acmeSales.SetPermissions([PermissionCatalog.OrganizationMembersRead], now);
        roleRepository.Add(acmeSales);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var alice = await CreateDemoUserAsync("Alice", "Martin", "alice@acme.test", "alice", options.DemoUserPassword, cancellationToken);
        var bob = await CreateDemoUserAsync("Bob", "Durand", "bob@globex.test", "bob", options.DemoUserPassword, cancellationToken);
        if (alice is null || bob is null)
        {
            return;
        }

        var aliceAcme = OrganizationMembership.Create(acme.Id, alice.Id, now);
        membershipRepository.Add(aliceAcme);
        membershipRepository.Add(OrganizationMembership.Create(globex.Id, alice.Id, now));
        var bobAcme = OrganizationMembership.Create(acme.Id, bob.Id, now);
        membershipRepository.Add(bobAcme);
        membershipRepository.Add(OrganizationMembership.Create(initech.Id, bob.Id, now));
        assignmentRepository.Add(UserRoleAssignment.ForOrganization(aliceAcme, acmeAdmin, null, now));
        assignmentRepository.Add(UserRoleAssignment.ForOrganization(bobAcme, acmeSales, null, now));

        var apiScope = ApiScope.Create("demo_api", "Demo API", "Access the demonstration API", "demo-api", now);
        scopeRepository.Add(apiScope);

        var application = ClientApplication.Create("Demo portal", "Demonstration web application", acme.Id, now);
        application.GrantOrganization(globex.Id, now);
        applicationRepository.Add(application);

        var baseUrl = options.DemoClientBaseUrl.TrimEnd('/');
        var client = OidcClient.Create(
            application.Id,
            "demo-client",
            "Demo portal",
            ClientType.Confidential,
            [$"{baseUrl}/signin-oidc"],
            [$"{baseUrl}/signout-callback-oidc"],
            [Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess, Scopes.OrganizationRoles, apiScope.Name],
            now);
        client.SetConsentPolicy(ConsentPolicy.Remembered, null, now);
        clientRepository.Add(client);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await clientRegistry.SyncScopeAsync(apiScope, cancellationToken);
        await clientRegistry.CreateAsync(client, options.DemoClientSecret, cancellationToken);
        logger.LogInformation("Development demo data seeded (organizations acme, globex, initech; users alice, bob; client demo-client).");
    }

    private async Task<User?> CreateDemoUserAsync(string firstName, string lastName, string email, string userName, string password, CancellationToken cancellationToken)
    {
        var user = User.Create(firstName, lastName, email, userName, clock.UtcNow);
        user.ConfirmEmail(clock.UtcNow);
        var result = await identityService.CreateUserAsync(user, password, cancellationToken);
        if (!result.Succeeded)
        {
            logger.LogError("Demo user {UserName} could not be created: {Errors}", userName, string.Join(" ", result.Errors));
            return null;
        }

        return user;
    }
}
