using Cerberus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Cerberus.Web.Infrastructure;

/// <summary>
/// Validates the server-side session on every authenticated request: a revoked or expired session, a disabled account
/// or a changed security stamp (password reset, email change...) immediately invalidates the cookie.
/// </summary>
public sealed class SessionValidationCookieEvents(IUserAuthenticationService authenticationService) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var userId = principal?.GetUserId();
        var sessionId = principal?.GetSessionId();
        var stamp = principal?.FindFirst(WebClaimTypes.SecurityStamp)?.Value;

        if (userId is null || sessionId is null || stamp is null
            || !await authenticationService.ValidateSessionAsync(userId.Value, sessionId.Value, stamp, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
