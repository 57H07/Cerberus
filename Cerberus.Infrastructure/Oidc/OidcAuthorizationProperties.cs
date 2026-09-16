namespace Cerberus.Infrastructure.Oidc;

public static class OidcAuthorizationProperties
{
    /// <summary>Property stored on every library authorization, used to revoke tokens per organization.</summary>
    public const string OrganizationId = "cerberus_org_id";
}
