using System.Security.Cryptography.X509Certificates;
using Cerberus.DemoClient;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
var oidc = builder.Configuration.GetSection(DemoOidcOptions.SectionName).Get<DemoOidcOptions>()
    ?? throw new InvalidOperationException("The Oidc section is missing.");

// In Docker, the browser reaches the IdP through the public URL while server-to-server calls use the internal network.
var backchannel = BackchannelHandler.Create(oidc, builder.Environment);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient("api").ConfigurePrimaryHttpMessageHandler(() => BackchannelHandler.CreateTrustHandler(oidc, builder.Environment));

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-demo";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = oidc.Authority;
        options.ClientId = oidc.ClientId;
        options.ClientSecret = oidc.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = true;
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.BackchannelHttpHandler = backchannel;
        options.Scope.Clear();
        foreach (var scope in oidc.Scopes)
        {
            options.Scope.Add(scope);
        }

        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "org_roles";
        options.Events.OnRedirectToIdentityProvider = context =>
        {
            // The tenant is only an intention: Cerberus validates it and returns the validated org_id claim.
            if (context.Properties.Items.TryGetValue("organization", out var organization) && !string.IsNullOrWhiteSpace(organization))
            {
                context.ProtocolMessage.SetParameter("organization", organization);
            }

            return Task.CompletedTask;
        };
        options.Events.OnTokenValidated = context =>
        {
            if (context.Principal?.FindFirst("org_id") is null)
            {
                context.Fail("The identity provider did not return a validated organization.");
            }

            return Task.CompletedTask;
        };
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.Authority = oidc.Authority;
        options.Audience = oidc.ApiAudience;
        options.MapInboundClaims = false;
        options.BackchannelHttpHandler = backchannel;
        options.TokenValidationParameters.ValidTypes = ["at+jwt"];
        options.TokenValidationParameters.NameClaimType = "sub";
        options.TokenValidationParameters.RoleClaimType = "org_roles";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(DemoPolicies.Api, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireClaim("org_id"));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();

namespace Cerberus.DemoClient
{
    public sealed class DemoOidcOptions
    {
        public const string SectionName = "Oidc";

        public string Authority { get; set; } = string.Empty;

        /// <summary>Optional internal base URL used for server-to-server calls (Docker network). Browser redirects keep using <see cref="Authority"/>.</summary>
        public string? BackchannelAuthority { get; set; }

        /// <summary>Optional PFX file whose certificate is trusted for back-channel TLS (development certificate in Docker).</summary>
        public string? BackchannelTrustedCertificatePath { get; set; }

        public string? BackchannelTrustedCertificatePassword { get; set; }

        public string ClientId { get; set; } = string.Empty;
        public string? ClientSecret { get; set; }
        public string ApiAudience { get; set; } = "demo-api";
        public string[] Scopes { get; set; } = ["openid", "profile", "email", "offline_access", "org.roles", "demo_api"];
    }

    public static class DemoPolicies
    {
        public const string Api = "api";
    }

    /// <summary>Rewrites back-channel requests from the public authority to the internal one and optionally pins a development certificate.</summary>
    public sealed class BackchannelHandler(Uri publicAuthority, Uri internalAuthority, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public static HttpMessageHandler Create(DemoOidcOptions options, IHostEnvironment environment)
        {
            var handler = CreateTrustHandler(options, environment);
            return string.IsNullOrEmpty(options.BackchannelAuthority)
                ? handler
                : new BackchannelHandler(new Uri(options.Authority), new Uri(options.BackchannelAuthority), handler);
        }

        public static HttpClientHandler CreateTrustHandler(DemoOidcOptions options, IHostEnvironment environment)
        {
            var handler = new HttpClientHandler();
            if (!string.IsNullOrEmpty(options.BackchannelTrustedCertificatePath))
            {
                if (!environment.IsDevelopment())
                {
                    throw new InvalidOperationException("Certificate pinning override is only allowed in Development.");
                }

                var trusted = X509CertificateLoader.LoadPkcs12FromFile(options.BackchannelTrustedCertificatePath, options.BackchannelTrustedCertificatePassword);
                handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                    errors == System.Net.Security.SslPolicyErrors.None || certificate?.Thumbprint == trusted.Thumbprint;
            }

            return handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri && publicAuthority.IsBaseOf(uri))
            {
                request.RequestUri = new Uri(internalAuthority, publicAuthority.MakeRelativeUri(uri));
            }

            var response = await base.SendAsync(request, cancellationToken);

            // The discovery document lists endpoints built from the request host (the internal one): browser-facing
            // endpoints must point to the public authority, back-channel calls are rewritten again by this handler.
            if (request.RequestUri?.AbsolutePath.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal) == true
                && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var rewritten = json.Replace(internalAuthority.GetLeftPart(UriPartial.Authority), publicAuthority.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal);
                response.Content = new StringContent(rewritten, System.Text.Encoding.UTF8, "application/json");
            }

            return response;
        }
    }
}
