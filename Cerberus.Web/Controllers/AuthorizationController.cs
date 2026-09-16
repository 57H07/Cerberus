using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Application.Oidc;
using Cerberus.Infrastructure.Oidc;
using Cerberus.Web.Infrastructure;
using Cerberus.Web.ViewModels;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Cerberus.Web.Controllers;

/// <summary>
/// OpenID Connect endpoints handled in passthrough mode. OpenIddict validates the protocol (client, redirect URI,
/// PKCE, scopes permissions, codes...) before these actions run; the actions only apply Cerberus business rules
/// (authentication, tenant validation, consent, claims) through the application layer.
/// </summary>
public class AuthorizationController(
    IOidcAuthorizationService authorizationService,
    IUserAuthenticationService authenticationService,
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IAntiforgery antiforgery) : Controller
{
    [HttpGet("~/connect/authorize"), HttpPost("~/connect/authorize"), IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var cookie = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!IsAuthenticationValid(request, cookie))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return ForbidOidc(Errors.LoginRequired, "The user is not signed in.");
            }

            return Challenge(
                new AuthenticationProperties { RedirectUri = BuildReturnUrl(excludePromptLogin: true) },
                CookieAuthenticationDefaults.AuthenticationScheme);
        }

        var userId = cookie.Principal!.RequireUserId();
        var dto = new AuthorizationRequestDto(
            userId,
            cookie.Principal!.GetSessionId(),
            request.ClientId!,
            request.GetScopes().ToList(),
            request[OidcParameters.Organization]?.ToString(),
            request.HasPromptValue(PromptValues.Consent));

        var submit = Request.HasFormContentType ? Request.Form["submit"].ToString() : string.Empty;
        if (submit is "accept" or "deny")
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            if (submit == "deny")
            {
                await authorizationService.RecordConsentDeniedAsync(dto, cancellationToken);
                return ForbidOidc(Errors.AccessDenied, "The user denied the authorization request.");
            }

            return await CompleteAsync(await authorizationService.GrantConsentAsync(dto, cancellationToken), request, cancellationToken);
        }

        var evaluation = await authorizationService.EvaluateAsync(dto, cancellationToken);
        switch (evaluation.Outcome)
        {
            case AuthorizationOutcome.OrganizationSelectionRequired when request.HasPromptValue(PromptValues.None):
                return ForbidOidc(Errors.InteractionRequired, "An organization must be selected.");
            case AuthorizationOutcome.OrganizationSelectionRequired:
                return View("SelectOrganization", new SelectOrganizationViewModel(evaluation.OrganizationOptions!, RequestParameters(OidcParameters.Organization)));
            case AuthorizationOutcome.ConsentRequired when request.HasPromptValue(PromptValues.None):
                return ForbidOidc(Errors.ConsentRequired, "Consent is required.");
            case AuthorizationOutcome.ConsentRequired:
                var parameters = RequestParameters(OidcParameters.Organization, "prompt")
                    .Append(new(OidcParameters.Organization, evaluation.Consent!.Organization.Slug))
                    .ToList();
                return View("Consent", new ConsentViewModel(evaluation.Consent, parameters));
            default:
                return await CompleteAsync(evaluation, request, cancellationToken);
        }
    }

    [HttpPost("~/connect/token"), IgnoreAntiforgeryToken, Produces("application/json"), EnableRateLimiting(RateLimitPolicies.Token)]
    public async Task<IActionResult> Exchange(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return ForbidOidc(Errors.UnsupportedGrantType, "The specified grant type is not supported.");
        }

        var principal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        if (principal is null
            || !Guid.TryParse(principal.GetClaim(Claims.Subject), out var userId)
            || !Guid.TryParse(principal.GetClaim(WebClaimTypes.ValidatedOrganization), out var organizationId))
        {
            return ForbidOidc(Errors.InvalidGrant, "The token is no longer valid.");
        }

        Guid? sessionId = Guid.TryParse(principal.GetClaim(WebClaimTypes.SessionId), out var sid) ? sid : null;
        var evaluation = await authorizationService.RevalidateAsync(
            userId, request.ClientId!, organizationId, principal.GetScopes().ToList(), sessionId, cancellationToken);
        if (evaluation.Outcome != AuthorizationOutcome.Granted)
        {
            return ForbidOidc(Errors.InvalidGrant, evaluation.ErrorDescription ?? "The token is no longer valid.");
        }

        var identity = BuildIdentity(evaluation.Subject!);
        identity.SetAuthorizationId(principal.GetAuthorizationId());
        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/connect/userinfo"), HttpPost("~/connect/userinfo"), IgnoreAntiforgeryToken, Produces("application/json")]
    [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    public async Task<IActionResult> UserInfo(CancellationToken cancellationToken)
    {
        var userId = Guid.TryParse(User.GetClaim(Claims.Subject), out var id) ? id : Guid.Empty;
        var profile = await authenticationService.GetProfileAsync(userId, cancellationToken);
        if (profile is null)
        {
            return Challenge(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user no longer exists."
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [Claims.Subject] = profile.Id.ToString()
        };

        if (User.GetClaim(CerberusClaimTypes.OrganizationId) is { } organizationId)
        {
            claims[CerberusClaimTypes.OrganizationId] = organizationId;
        }

        if (User.HasScope(Domain.Clients.Scopes.Profile))
        {
            claims[Claims.Name] = $"{profile.FirstName} {profile.LastName}";
            claims[Claims.GivenName] = profile.FirstName;
            claims[Claims.FamilyName] = profile.LastName;
            claims[Claims.PreferredUsername] = profile.UserName;
        }

        if (User.HasScope(Domain.Clients.Scopes.Email))
        {
            claims[Claims.Email] = profile.Email;
            claims[Claims.EmailVerified] = profile.EmailConfirmed;
        }

        return Ok(claims);
    }

    [HttpGet("~/connect/endsession"), IgnoreAntiforgeryToken]
    public async Task<IActionResult> EndSession()
    {
        var cookie = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!cookie.Succeeded)
        {
            return SignOut(new AuthenticationProperties { RedirectUri = "/" }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // A confirmation is always requested: it prevents cross-site logout of the user.
        return View(new EndSessionViewModel(RequestParameters()));
    }

    [HttpPost("~/connect/endsession"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EndSessionConfirmed(CancellationToken cancellationToken)
    {
        if (User.GetUserId() is { } userId && User.GetSessionId() is { } sessionId)
        {
            await authenticationService.LogoutAsync(userId, sessionId, cancellationToken);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // OpenIddict redirects to the validated post_logout_redirect_uri, or to "/" when none was provided.
        return SignOut(new AuthenticationProperties { RedirectUri = "/" }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> CompleteAsync(AuthorizationEvaluationDto evaluation, OpenIddictRequest request, CancellationToken cancellationToken)
    {
        if (evaluation.Outcome != AuthorizationOutcome.Granted)
        {
            return ForbidOidc(evaluation.Error ?? Errors.AccessDenied, evaluation.ErrorDescription ?? "The authorization was denied.");
        }

        var subject = evaluation.Subject!;
        var identity = BuildIdentity(subject);
        identity.SetAuthorizationId(await GetOrCreateAuthorizationAsync(subject, request, identity, cancellationToken));
        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<string> GetOrCreateAuthorizationAsync(TokenSubject subject, OpenIddictRequest request, ClaimsIdentity identity, CancellationToken cancellationToken)
    {
        if (subject.AuthorizationId is not null
            && await authorizationManager.FindByIdAsync(subject.AuthorizationId, cancellationToken) is { } existing
            && await authorizationManager.HasStatusAsync(existing, Statuses.Valid, cancellationToken))
        {
            return subject.AuthorizationId;
        }

        var application = await applicationManager.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException("The client application cannot be found.");

        var descriptor = new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = await applicationManager.GetIdAsync(application, cancellationToken),
            Subject = subject.UserId.ToString(),
            Principal = new ClaimsPrincipal(identity),
            Status = Statuses.Valid,
            Type = subject.IsRememberedConsent ? AuthorizationTypes.Permanent : AuthorizationTypes.AdHoc,
            CreationDate = DateTimeOffset.UtcNow
        };
        descriptor.Scopes.UnionWith(subject.Scopes);
        descriptor.Properties[OidcAuthorizationProperties.OrganizationId] = JsonSerializer.SerializeToElement(subject.OrganizationId.ToString());

        var authorization = await authorizationManager.CreateAsync(descriptor, cancellationToken);
        var authorizationId = (await authorizationManager.GetIdAsync(authorization, cancellationToken))!;
        if (subject.IsRememberedConsent)
        {
            await authorizationService.LinkAuthorizationAsync(subject.UserId, subject.OidcClientId, subject.OrganizationId, authorizationId, cancellationToken);
        }

        return authorizationId;
    }

    private static ClaimsIdentity BuildIdentity(TokenSubject subject)
    {
        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        foreach (var claim in subject.Claims)
        {
            var destinations = new List<string>(2);
            if (claim.Destinations.HasFlag(ClaimDestinations.IdentityToken))
            {
                destinations.Add(Destinations.IdentityToken);
            }

            if (claim.Destinations.HasFlag(ClaimDestinations.AccessToken))
            {
                destinations.Add(Destinations.AccessToken);
            }

            identity.AddClaim(new Claim(claim.Type, claim.Value).SetDestinations(destinations.ToImmutableArray()));
        }

        // Private claim: no destination, only kept inside the encrypted authorization code / refresh token.
        identity.AddClaim(new Claim(WebClaimTypes.ValidatedOrganization, subject.OrganizationId.ToString()));
        identity.SetScopes(subject.Scopes);
        identity.SetResources(subject.Resources);
        return identity;
    }

    private static bool IsAuthenticationValid(OpenIddictRequest request, AuthenticateResult cookie)
    {
        if (!cookie.Succeeded || cookie.Principal?.GetUserId() is null)
        {
            return false;
        }

        if (request.HasPromptValue(PromptValues.Login))
        {
            return false;
        }

        return request.MaxAge is null
            || cookie.Properties?.IssuedUtc is null
            || DateTimeOffset.UtcNow - cookie.Properties.IssuedUtc <= TimeSpan.FromSeconds(request.MaxAge.Value);
    }

    private IActionResult ForbidOidc(string error, string description)
        => Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    private string BuildReturnUrl(bool excludePromptLogin)
    {
        var parameters = RequestParameters()
            .Where(p => !(excludePromptLogin && p.Key == Parameters.Prompt && p.Value.Contains(PromptValues.Login)))
            .Where(p => !(excludePromptLogin && p.Key == Parameters.MaxAge));
        return Request.PathBase + Request.Path + QueryString.Create(parameters!);
    }

    private IReadOnlyList<KeyValuePair<string, string>> RequestParameters(params string[] excluded)
    {
        var source = Request.HasFormContentType
            ? Request.Form.Where(p => p.Key is not "submit" and not "__RequestVerificationToken")
            : Request.Query.AsEnumerable();
        return source
            .Where(p => !excluded.Contains(p.Key))
            .SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string>(p.Key, v ?? string.Empty)))
            .ToList();
    }
}
