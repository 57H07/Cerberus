using Cerberus.Application.Authorization;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Application.Oidc;
using Cerberus.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cerberus.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAccessGuard, AccessGuard>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<IOidcAuthorizationService, OidcAuthorizationService>();
        services.AddScoped<IOrganizationManagementService, OrganizationManagementService>();
        services.AddScoped<IPlatformAdministrationService, PlatformAdministrationService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IClientAdministrationService, ClientAdministrationService>();
        services.AddScoped<IUserSelfServiceService, UserSelfServiceService>();
        return services;
    }
}
