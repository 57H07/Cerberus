using Cerberus.Application.Common;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Security;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Domain.Users;
using Cerberus.Infrastructure.Data;
using Cerberus.Infrastructure.Identity;
using Cerberus.Infrastructure.Oidc;
using Cerberus.Infrastructure.Repositories;
using Cerberus.Infrastructure.Security;
using Cerberus.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cerberus.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        services.AddDbContext<CerberusDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
            options.UseOpenIddict<Guid>();
        });

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));
        services.Configure<DemoSeedOptions>(configuration.GetSection(DemoSeedOptions.SectionName));
        services.Configure<SessionOptions>(configuration.GetSection(SessionOptions.SectionName));
        services.Configure<InvitationOptions>(configuration.GetSection(InvitationOptions.SectionName));
        services.Configure<KeyManagementOptions>(configuration.GetSection(KeyManagementOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IClientApplicationRepository, ClientApplicationRepository>();
        services.AddScoped<IOidcClientRepository, OidcClientRepository>();
        services.AddScoped<IApiScopeRepository, ApiScopeRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRoleAssignmentRepository, RoleAssignmentRepository>();
        services.AddScoped<IUserConsentRepository, UserConsentRepository>();
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<ISigningKeyRepository, SigningKeyRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        // Data Protection keys are stored in the database: they protect cookies, Identity tokens and the private signing keys,
        // so they must survive restarts and be shared between instances.
        services.AddDataProtection()
            .SetApplicationName("Cerberus")
            .PersistKeysToDbContext<CerberusDbContext>();

        services.AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddUserStore<UserStore>()
            .AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(2));
        services.AddScoped<ILookupNormalizer, DomainLookupNormalizer>();
        services.AddScoped<IIdentityService, IdentityService>();

        services.AddScoped<ITokenRevocationService, TokenRevocationService>();
        services.AddScoped<IOidcClientRegistry, OidcClientRegistry>();
        services.AddScoped<SigningKeyManager>();
        services.AddSingleton<KeyRotationHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<KeyRotationHostedService>());
        services.AddScoped<DatabaseInitializer>();

        return services;
    }
}
