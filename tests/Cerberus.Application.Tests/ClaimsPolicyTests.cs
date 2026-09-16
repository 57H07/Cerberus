using Cerberus.Application.Oidc;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;

namespace Cerberus.Application.Tests;

public class ClaimsPolicyTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly User _user = User.Create("Alice", "Martin", "alice@example.com", "alice", Now);
    private readonly Organization _organization = Organization.Create("Acme", "acme", Now);

    [Fact]
    public void Build_OpenIdOnly_ShouldOnlyReleaseSubjectAndOrganization()
    {
        var claims = ClaimsPolicy.Build(_user, _organization, [Scopes.OpenId], ["Admin"], null);

        claims.Select(c => c.Type).Should().BeEquivalentTo([CerberusClaimTypes.Subject, CerberusClaimTypes.OrganizationId, CerberusClaimTypes.OrganizationSlug]);
    }

    [Fact]
    public void Build_AccessToken_ShouldNeverContainProfileOrEmail()
    {
        var claims = ClaimsPolicy.Build(_user, _organization, [Scopes.OpenId, Scopes.Profile, Scopes.Email], [], Guid.NewGuid());

        claims.Where(c => c.Destinations.HasFlag(ClaimDestinations.AccessToken)).Select(c => c.Type)
            .Should().BeEquivalentTo([CerberusClaimTypes.Subject, CerberusClaimTypes.OrganizationId]);
        claims.Should().Contain(c => c.Type == CerberusClaimTypes.Email && c.Destinations == ClaimDestinations.IdentityToken);
        claims.Should().Contain(c => c.Type == CerberusClaimTypes.SessionId && c.Destinations == ClaimDestinations.IdentityToken);
    }

    [Fact]
    public void Build_OrganizationRolesScope_ShouldReleaseDistinctRolesToBothTokens()
    {
        var claims = ClaimsPolicy.Build(_user, _organization, [Scopes.OpenId, Scopes.OrganizationRoles], ["Sales", "Sales", "Support"], null);

        claims.Where(c => c.Type == CerberusClaimTypes.OrganizationRoles).Select(c => c.Value).Should().BeEquivalentTo(["Sales", "Support"]);
        claims.Where(c => c.Type == CerberusClaimTypes.OrganizationRoles)
            .Should().OnlyContain(c => c.Destinations == (ClaimDestinations.AccessToken | ClaimDestinations.IdentityToken));
    }

    [Fact]
    public void Build_WithoutOrganizationRolesScope_ShouldNotReleaseRoles()
    {
        var claims = ClaimsPolicy.Build(_user, _organization, [Scopes.OpenId, Scopes.Profile], ["Admin"], null);

        claims.Should().NotContain(c => c.Type == CerberusClaimTypes.OrganizationRoles);
    }

    [Fact]
    public void Build_EmailVerified_ShouldReflectConfirmation()
    {
        _user.ConfirmEmail(Now);

        var claims = ClaimsPolicy.Build(_user, _organization, [Scopes.OpenId, Scopes.Email], [], null);

        claims.Single(c => c.Type == CerberusClaimTypes.EmailVerified).Value.Should().Be("true");
    }
}
