namespace Cerberus.Web.Infrastructure;

public static class WebClaimTypes
{
    public const string UserId = "sub";
    public const string SessionId = "sid";
    public const string SecurityStamp = "cerberus_security_stamp";
    public const string Name = "name";

    /// <summary>Private claim kept in authorization codes and refresh tokens only (no destination): the validated organization.</summary>
    public const string ValidatedOrganization = "cerberus_org";
}

public static class RateLimitPolicies
{
    public const string Authentication = "authentication";
    public const string Token = "token";
}
