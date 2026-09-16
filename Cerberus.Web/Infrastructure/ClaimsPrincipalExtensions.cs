using System.Security.Claims;

namespace Cerberus.Web.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(WebClaimTypes.UserId), out var id) ? id : null;

    public static Guid? GetSessionId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(WebClaimTypes.SessionId), out var id) ? id : null;

    public static Guid RequireUserId(this ClaimsPrincipal principal)
        => principal.GetUserId() ?? throw new UnauthorizedAccessException("The user is not authenticated.");
}
