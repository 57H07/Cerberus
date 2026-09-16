using Cerberus.Domain.Applications;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;

namespace Cerberus.Domain.Tests;

internal static class TestData
{
    public static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    public static User User(string login = "alice", string email = "alice@example.com")
        => Domain.Users.User.Create("Alice", "Martin", email, login, Now);

    public static Organization Organization(string slug = "acme")
        => Domain.Organizations.Organization.Create("Acme", slug, Now);

    public static ClientApplication Application(Guid ownerOrganizationId)
        => ClientApplication.Create("CRM", null, ownerOrganizationId, Now);

    public static OidcClient Client(Guid applicationId, params string[] scopes)
        => OidcClient.Create(
            applicationId,
            "crm-web",
            "CRM Web",
            ClientType.Confidential,
            ["https://crm.example.com/signin-oidc"],
            ["https://crm.example.com/signout-callback-oidc"],
            scopes.Length == 0 ? [Scopes.OpenId, Scopes.Profile] : scopes,
            Now);
}
