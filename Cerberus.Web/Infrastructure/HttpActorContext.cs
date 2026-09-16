using Cerberus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Cerberus.Web.Infrastructure;

/// <summary>
/// Actor of the current request. Only the interactive cookie identity is considered: an access token presented to
/// the IdP never grants administrative rights.
/// </summary>
public sealed class HttpActorContext(IHttpContextAccessor accessor) : IActorContext
{
    public Guid? UserId
    {
        get
        {
            var user = accessor.HttpContext?.User;
            return user?.Identity is { IsAuthenticated: true, AuthenticationType: CookieAuthenticationDefaults.AuthenticationScheme }
                ? user.GetUserId()
                : null;
        }
    }

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, 512)];
        }
    }
}
