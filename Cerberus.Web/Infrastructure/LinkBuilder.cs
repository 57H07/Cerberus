using Cerberus.Application.Interfaces.Security;

namespace Cerberus.Web.Infrastructure;

public sealed class LinkBuilder(IHttpContextAccessor accessor, LinkGenerator linkGenerator) : ILinkBuilder
{
    public string PasswordReset(Guid userId, string token)
        => Build("ResetPassword", "Account", new { userId, token });

    public string EmailConfirmation(Guid userId, string token)
        => Build("ConfirmEmail", "Account", new { userId, token });

    public string Invitation(string token)
        => Build("Invitation", "Account", new { token });

    private string Build(string action, string controller, object values)
    {
        var httpContext = accessor.HttpContext ?? throw new InvalidOperationException("Links can only be built during an HTTP request.");
        return linkGenerator.GetUriByAction(httpContext, action, controller, values)
            ?? throw new InvalidOperationException($"No route for {controller}.{action}.");
    }
}
