using Cerberus.Domain.Applications;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Tests.Clients;

public class OidcClientTests
{
    [Theory]
    [InlineData("https://app.example.com/callback")]
    [InlineData("http://localhost:5003/signin-oidc")]
    [InlineData("http://127.0.0.1:8080/cb")]
    public void RedirectUri_Valid_ShouldBeAccepted(string uri)
    {
        RedirectUriRules.Validate(uri, "uri").Should().Be(uri);
    }

    [Theory]
    [InlineData("https://*.example.com/callback")]
    [InlineData("http://app.example.com/callback")]
    [InlineData("/relative/callback")]
    [InlineData("https://app.example.com/callback#frag")]
    [InlineData("https://user:pass@app.example.com/callback")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void RedirectUri_Invalid_ShouldBeRejected(string uri)
    {
        var act = () => RedirectUriRules.Validate(uri, "uri");

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Create_WithoutRedirectUri_ShouldThrow()
    {
        var act = () => OidcClient.Create(Guid.NewGuid(), "client", "Client", ClientType.Public, [], [], [Scopes.OpenId], TestData.Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Create_ShouldAlwaysIncludeOpenIdScope()
    {
        var client = TestData.Client(Guid.NewGuid(), Scopes.Profile);

        client.AllowedScopes.Should().Contain(Scopes.OpenId).And.Contain(Scopes.Profile);
        client.AllowsRefreshTokens.Should().BeFalse();
    }

    [Fact]
    public void Create_WithInvalidScopeName_ShouldThrow()
    {
        var act = () => TestData.Client(Guid.NewGuid(), "Bad Scope");

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void SetConsentPolicy_LifetimeWithoutRememberedPolicy_ShouldThrow()
    {
        var client = TestData.Client(Guid.NewGuid());

        var act = () => client.SetConsentPolicy(ConsentPolicy.AlwaysPrompt, TimeSpan.FromDays(30), TestData.Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void ApiScope_WithStandardName_ShouldThrow()
    {
        var act = () => ApiScope.Create(Scopes.Profile, "Profile", null, "api", TestData.Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Application_OwnerAccess_CannotBeRemoved()
    {
        var ownerId = Guid.NewGuid();
        var application = ClientApplication.Create("CRM", null, ownerId, TestData.Now);

        var act = () => application.RemoveOrganization(ownerId, TestData.Now);

        act.Should().Throw<InvalidDomainOperationException>();
        application.IsAvailableTo(ownerId).Should().BeTrue();
    }

    [Fact]
    public void Application_Disabled_ShouldNotBeAvailable()
    {
        var ownerId = Guid.NewGuid();
        var application = ClientApplication.Create("CRM", null, ownerId, TestData.Now);

        application.Disable(TestData.Now);

        application.IsAvailableTo(ownerId).Should().BeFalse();
    }
}
