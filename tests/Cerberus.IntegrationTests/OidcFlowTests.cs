using System.Net;
using Cerberus.Application.Oidc;
using Cerberus.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cerberus.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class OidcFlowTests(CerberusWebFactory factory)
{
    private readonly TestScenario _scenario = new(factory);

    [Fact]
    public async Task Discovery_ShouldOnlyAdvertiseCodeFlowWithPkce()
    {
        var client = factory.CreateBrowser();

        var json = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/.well-known/openid-configuration")).RootElement;

        json.GetProperty("issuer").GetString().Should().Be(CerberusWebFactory.Issuer);
        json.GetProperty("response_types_supported").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(["code"]);
        json.GetProperty("grant_types_supported").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["authorization_code", "refresh_token"]);
        json.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString()).Should().Contain("S256");
    }

    [Fact]
    public async Task FullFlow_ShouldIssueSignedTokensWithMinimalClaims()
    {
        var (organization, user) = await MemberAsync();
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);

        var code = await flow.AuthorizeAndGetCodeAsync($"openid profile {_scenario.ApiScope}");
        var tokens = await flow.RedeemAsync(code);

        tokens.Succeeded.Should().BeTrue(tokens.Json.ToString());
        var access = await ValidateAsync(flow, tokens.AccessToken, _scenario.ApiResource);
        access.Subject.Should().Be(user.Id.ToString());
        access.GetClaim(CerberusClaimTypes.OrganizationId).Value.Should().Be(organization.Id.ToString());
        access.TryGetClaim(CerberusClaimTypes.Email, out _).Should().BeFalse();
        access.TryGetClaim(CerberusClaimTypes.OrganizationRoles, out _).Should().BeFalse("org.roles was not requested");
        access.TryGetClaim("cerberus_org", out _).Should().BeFalse("private claims never leave the server");
        access.TryGetClaim("role", out _).Should().BeFalse();

        var id = OidcFlow.ReadJwt(tokens.IdToken);
        id.GetClaim("nonce").Value.Should().Be(flow.Nonce);
        id.GetClaim(CerberusClaimTypes.GivenName).Value.Should().Be(user.FirstName);
        id.TryGetClaim(CerberusClaimTypes.Email, out _).Should().BeFalse("email scope was not requested");
        id.GetClaim(CerberusClaimTypes.OrganizationSlug).Value.Should().Be(organization.Slug);
        tokens.RefreshToken.Should().BeNull("offline_access was not requested");
    }

    [Fact]
    public async Task OrganizationRolesScope_ShouldReleaseOnlyRolesOfSelectedOrganization()
    {
        var acme = await _scenario.CreateOrganizationAsync("acme");
        var globex = await _scenario.CreateOrganizationAsync("globex");
        var user = await _scenario.CreateUserAsync("multi");
        await _scenario.CreateOrganizationRoleAsync(acme, "Sales");
        await _scenario.CreateOrganizationRoleAsync(globex, "Auditors");
        await _scenario.AddMemberAsync(acme, user, "Sales");
        await _scenario.AddMemberAsync(globex, user, "Auditors");
        await _scenario.CreateClientAsync(acme, grantedOrganizations: globex);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);

        var selection = await flow.AuthorizeAsync(flow.AuthorizeUrl("openid org.roles"));
        (await selection.Content.ReadAsStringAsync()).Should().Contain(acme.Slug).And.Contain(globex.Slug);

        var acmeTokens = await flow.RedeemAsync(await flow.AuthorizeAndGetCodeAsync("openid org.roles", acme.Slug));
        flow.ResetPkce();
        var globexTokens = await flow.RedeemAsync(await flow.AuthorizeAndGetCodeAsync("openid org.roles", globex.Slug));

        var acmeAccess = OidcFlow.ReadJwt(acmeTokens.AccessToken);
        acmeAccess.GetClaim(CerberusClaimTypes.OrganizationId).Value.Should().Be(acme.Id.ToString());
        acmeAccess.Claims.Where(c => c.Type == CerberusClaimTypes.OrganizationRoles).Select(c => c.Value).Should().BeEquivalentTo(["Sales"]);
        var globexAccess = OidcFlow.ReadJwt(globexTokens.AccessToken);
        globexAccess.GetClaim(CerberusClaimTypes.OrganizationId).Value.Should().Be(globex.Id.ToString());
        globexAccess.Claims.Where(c => c.Type == CerberusClaimTypes.OrganizationRoles).Select(c => c.Value).Should().BeEquivalentTo(["Auditors"]);
    }

    [Fact]
    public async Task UnknownOrganization_ShouldBeDenied()
    {
        var (organization, user) = await MemberAsync();
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);

        var response = await flow.AuthorizeAsync(flow.AuthorizeUrl(organization: "does-not-exist-org"));

        OidcFlow.RedirectParameters(response)["error"].Should().Be("access_denied");
    }

    [Fact]
    public async Task OrganizationNotGrantedToApplication_ShouldBeDenied()
    {
        var (owner, user) = await MemberAsync();
        var other = await _scenario.CreateOrganizationAsync("other");
        await _scenario.AddMemberAsync(other, user);
        await _scenario.CreateClientAsync(owner);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);

        var response = await flow.AuthorizeAsync(flow.AuthorizeUrl(organization: other.Slug));

        OidcFlow.RedirectParameters(response)["error"].Should().Be("access_denied");
    }

    [Fact]
    public async Task NonMember_ShouldBeDeniedEvenWithOrganizationId()
    {
        var owner = await _scenario.CreateOrganizationAsync("owner");
        var outsider = await _scenario.CreateUserAsync("outsider");
        await _scenario.CreateClientAsync(owner);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(outsider.UserName);

        var bySlug = await flow.AuthorizeAsync(flow.AuthorizeUrl(organization: owner.Slug));
        var byId = await flow.AuthorizeAsync(flow.AuthorizeUrl(organization: owner.Id.ToString()));

        OidcFlow.RedirectParameters(bySlug)["error"].Should().Be("access_denied");
        OidcFlow.RedirectParameters(byId)["error"].Should().Be("access_denied");
    }

    [Fact]
    public async Task ConsentForm_TamperedOrganization_ShouldBeRevalidated()
    {
        var (owner, user) = await MemberAsync();
        var foreign = await _scenario.CreateOrganizationAsync("foreign");
        await _scenario.CreateClientAsync(owner, grantedOrganizations: foreign);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);

        var consent = await flow.AuthorizeAsync(flow.AuthorizeUrl(organization: owner.Slug));
        var response = await flow.SubmitFormAsync(consent, "accept", new Dictionary<string, string> { ["organization"] = foreign.Slug });

        OidcFlow.RedirectParameters(response)["error"].Should().Be("access_denied");
    }

    [Fact]
    public async Task InvalidRedirectUri_ShouldNotRedirect()
    {
        var (organization, user) = await MemberAsync();
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);

        var response = await flow.Browser.GetAsync(flow.AuthorizeUrl(redirectUri: "https://evil.example.com/callback"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task MissingPkce_ShouldBeRejected()
    {
        var (organization, user) = await MemberAsync();
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);

        var response = await flow.Browser.GetAsync(flow.AuthorizeUrl(pkce: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();
        (await response.Content.ReadAsStringAsync()).Should().Contain("code_challenge");
    }

    [Fact]
    public async Task WrongCodeVerifier_ShouldBeRejected()
    {
        var (organization, user) = await MemberAsync();
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);
        var code = await flow.AuthorizeAndGetCodeAsync();

        var tokens = await flow.RedeemAsync(code, codeVerifier: Base64UrlEncoder.Encode(Guid.NewGuid().ToByteArray()) + "padding-padding-padding");

        tokens.Succeeded.Should().BeFalse();
        tokens.Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task ReusedAuthorizationCode_ShouldBeRejectedAndRevokeIssuedTokens()
    {
        var (organization, user) = await MemberAsync();
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);
        var code = await flow.AuthorizeAndGetCodeAsync("openid offline_access");
        var first = await flow.RedeemAsync(code);

        var second = await flow.RedeemAsync(code);

        first.Succeeded.Should().BeTrue();
        second.Error.Should().Be("invalid_grant");
        (await flow.RefreshAsync(first.RefreshToken!)).Error.Should().Be("invalid_grant", "tokens issued from a replayed code are revoked");
    }

    [Fact]
    public async Task WrongClientSecret_ShouldBeRejected()
    {
        var (organization, user) = await MemberAsync();
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);
        var code = await flow.AuthorizeAndGetCodeAsync();

        var tokens = await flow.RedeemAsync(code, clientSecret: "wrong-secret");

        tokens.Error.Should().Be("invalid_client");
    }

    [Fact]
    public async Task ExpiredAccessToken_ShouldFailValidation()
    {
        var (organization, user) = await MemberAsync();
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);
        var tokens = await flow.RedeemAsync(await flow.AuthorizeAndGetCodeAsync($"openid {_scenario.ApiScope}"));
        var jwt = OidcFlow.ReadJwt(tokens.AccessToken);

        (jwt.ValidTo - jwt.IssuedAt).Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5));
        var result = await ValidateRawAsync(flow, tokens.AccessToken, _scenario.ApiResource, jwt.ValidTo.AddMinutes(1));
        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeAssignableTo<SecurityTokenInvalidLifetimeException>();
    }

    [Fact]
    public async Task AccessToken_ForOtherAudience_ShouldFailValidation()
    {
        var (organization, user) = await MemberAsync();
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);
        var tokens = await flow.RedeemAsync(await flow.AuthorizeAndGetCodeAsync($"openid {_scenario.ApiScope}"));

        var result = await ValidateRawAsync(flow, tokens.AccessToken, "another-api", DateTime.UtcNow);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task PromptNone_WithoutSession_ShouldReturnLoginRequired()
    {
        var (organization, _) = await MemberAsync();
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);

        var response = await flow.Browser.GetAsync(flow.AuthorizeUrl(prompt: "none"));

        OidcFlow.RedirectParameters(response)["error"].Should().Be("login_required");
    }

    private async Task<(Domain.Organizations.Organization, Domain.Users.User)> MemberAsync()
    {
        var organization = await _scenario.CreateOrganizationAsync("org");
        var user = await _scenario.CreateUserAsync("user");
        await _scenario.AddMemberAsync(organization, user);
        return (organization, user);
    }

    private static async Task<JsonWebToken> ValidateAsync(OidcFlow flow, string token, string audience)
    {
        var result = await ValidateRawAsync(flow, token, audience, DateTime.UtcNow);
        result.IsValid.Should().BeTrue(result.Exception?.Message);
        return (JsonWebToken)result.SecurityToken;
    }

    internal static async Task<TokenValidationResult> ValidateRawAsync(OidcFlow flow, string token, string audience, DateTime now)
    {
        var jwks = new JsonWebKeySet(await flow.Browser.GetStringAsync("/.well-known/jwks"));
        return await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = CerberusWebFactory.Issuer,
            ValidAudience = audience,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidTypes = ["at+jwt"],
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (notBefore, expires, _, _) => expires > now && (notBefore is null || notBefore <= now.AddSeconds(5))
        });
    }
}
