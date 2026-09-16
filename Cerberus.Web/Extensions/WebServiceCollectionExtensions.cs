using System.Threading.RateLimiting;
using Cerberus.Application.Interfaces.Security;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Domain.Clients;
using Cerberus.Infrastructure.Data;
using Cerberus.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Cerberus.Web.Extensions;

public sealed class OidcServerOptions
{
    public const string SectionName = "Oidc";

    /// <summary>Public issuer URI (e.g. https://localhost:5001/). When empty, the request host is used (development only).</summary>
    public string? Issuer { get; set; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan IdentityTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromHours(8);
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "Security:RateLimiting";

    public int AuthenticationPermitsPerMinute { get; set; } = 10;
    public int TokenPermitsPerMinute { get; set; } = 60;
}

public static class WebServiceCollectionExtensions
{
    public static IServiceCollection AddWeb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IActorContext, HttpActorContext>();
        services.AddScoped<ILinkBuilder, LinkBuilder>();
        services.AddScoped<SessionValidationCookieEvents>();
        services.Configure<OidcServerOptions>(configuration.GetSection(OidcServerOptions.SectionName));

        services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = "__Host-cerberus";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.LoginPath = "/Account/Login";
                options.LogoutPath = "/Account/Logout";
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.EventsType = typeof(SessionValidationCookieEvents);
            });

        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-cerberus-af";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        services.Configure<CookiePolicyOptions>(options =>
        {
            options.MinimumSameSitePolicy = SameSiteMode.Lax;
            options.Secure = CookieSecurePolicy.Always;
        });

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>()?.ToList()
                .ForEach(proxy => options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy)));
        });

        var rateLimits = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicies.Authentication, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = rateLimits.AuthenticationPermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
            options.AddPolicy(RateLimitPolicies.Token, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = rateLimits.TokenPermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
        });

        services.AddOidcServer(configuration);
        services.AddProblemDetails();
        return services;
    }

    private static void AddOidcServer(this IServiceCollection services, IConfiguration configuration)
    {
        var oidc = configuration.GetSection(OidcServerOptions.SectionName).Get<OidcServerOptions>() ?? new OidcServerOptions();

        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<CerberusDbContext>().ReplaceDefaultEntities<Guid>())
            .AddServer(options =>
            {
                if (!string.IsNullOrWhiteSpace(oidc.Issuer))
                {
                    options.SetIssuer(new Uri(oidc.Issuer, UriKind.Absolute));
                }

                options.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetEndSessionEndpointUris("connect/endsession")
                    .SetRevocationEndpointUris("connect/revocation");

                // Authorization code (+ PKCE) and refresh token only: no implicit, hybrid, password or client credentials flow.
                options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
                options.RequireProofKeyForCodeExchange();

                options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess, Scopes.OrganizationRoles);
                options.RegisterClaims("sub", "name", "given_name", "family_name", "preferred_username", "email", "email_verified",
                    "sid", "org_id", "org_slug", "org_roles");

                options.SetAccessTokenLifetime(oidc.AccessTokenLifetime)
                    .SetIdentityTokenLifetime(oidc.IdentityTokenLifetime)
                    .SetAuthorizationCodeLifetime(oidc.AuthorizationCodeLifetime)
                    .SetRefreshTokenLifetime(oidc.RefreshTokenLifetime);

                // Access tokens are signed JWTs readable by resource servers (validated with JWKS); codes and refresh tokens stay encrypted.
                options.DisableAccessTokenEncryption();

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>, OpenIddictKeyConfigurator>();
    }
}
