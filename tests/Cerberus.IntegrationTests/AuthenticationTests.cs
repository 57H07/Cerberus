using System.Net;
using System.Text.RegularExpressions;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Domain.Keys;
using Cerberus.Infrastructure.Security;
using Cerberus.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Cerberus.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public partial class AuthenticationTests(CerberusWebFactory factory)
{
    private readonly TestScenario _scenario = new(factory);

    [Fact]
    public async Task Login_WithValidCredentials_ShouldSignIn()
    {
        var user = await _scenario.CreateUserAsync("valid");
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);

        var response = await flow.LoginAsync(user.UserName);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await flow.Browser.GetAsync("/Account/Manage")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_WithEmailInDifferentCase_ShouldSignIn()
    {
        var user = await _scenario.CreateUserAsync("email");
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);

        var response = await flow.LoginAsync(user.Email.ToUpperInvariant());

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Login_WithWrongPasswordOrUnknownUser_ShouldReturnSameGenericError()
    {
        var user = await _scenario.CreateUserAsync("wrong");
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);

        var wrongPassword = await flow.LoginAsync(user.UserName, "not-the-password-123");
        var unknownUser = await flow.LoginAsync("nobody-" + Guid.NewGuid().ToString("N")[..8]);

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.OK);
        (await wrongPassword.Content.ReadAsStringAsync()).Should().Contain("Invalid login or password.");
        (await unknownUser.Content.ReadAsStringAsync()).Should().Contain("Invalid login or password.");
        (await flow.Browser.GetAsync("/Account/Manage")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Login_AfterFiveFailures_ShouldLockOutEvenWithCorrectPassword()
    {
        var user = await _scenario.CreateUserAsync("lock");
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        for (var i = 0; i < 5; i++)
        {
            await flow.LoginAsync(user.UserName, "not-the-password-123");
        }

        var response = await flow.LoginAsync(user.UserName);

        (await response.Content.ReadAsStringAsync()).Should().Contain("Too many failed attempts");
    }

    [Fact]
    public async Task Login_DisabledAccount_ShouldBeRefused()
    {
        var user = await _scenario.CreateUserAsync("disabled");
        await DisableUserAsync(user.Id);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);

        var response = await flow.LoginAsync(user.UserName);

        (await response.Content.ReadAsStringAsync()).Should().Contain("This account is disabled.");
    }

    [Fact]
    public async Task Logout_ShouldRevokeServerSession_EvenIfOldCookieIsReplayed()
    {
        var user = await _scenario.CreateUserAsync("logout");
        var raw = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        var loginPage = await raw.GetAsync("/Account/Login");
        var antiforgeryCookie = Cookies(loginPage);
        var login = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Login"] = user.UserName,
                ["Password"] = TestScenario.Password,
                ["__RequestVerificationToken"] = OidcFlow.ExtractAntiforgery(await loginPage.Content.ReadAsStringAsync())
            })
        };
        login.Headers.Add("Cookie", antiforgeryCookie);
        var sessionCookie = Cookies(await raw.SendAsync(login));
        sessionCookie.Should().Contain("__Host-cerberus=");

        var manage = await SendWithCookieAsync(raw, HttpMethod.Get, "/Account/Manage", $"{antiforgeryCookie}; {sessionCookie}");
        manage.StatusCode.Should().Be(HttpStatusCode.OK);
        await SendWithCookieAsync(raw, HttpMethod.Post, "/Account/Logout", $"{antiforgeryCookie}; {sessionCookie}",
            new Dictionary<string, string> { ["__RequestVerificationToken"] = OidcFlow.ExtractAntiforgery(await manage.Content.ReadAsStringAsync()) });

        var replay = await SendWithCookieAsync(raw, HttpMethod.Get, "/Account/Manage", $"{antiforgeryCookie}; {sessionCookie}");
        replay.StatusCode.Should().Be(HttpStatusCode.Redirect, "the replayed cookie refers to a revoked server session");
    }

    [Fact]
    public async Task DisablingUser_ShouldInvalidateSessionsAndRefreshTokens()
    {
        var organization = await _scenario.CreateOrganizationAsync("org");
        var user = await _scenario.CreateUserAsync("user");
        await _scenario.AddMemberAsync(organization, user);
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);
        var tokens = await flow.RedeemAsync(await flow.AuthorizeAndGetCodeAsync("openid offline_access"));

        await DisableUserAsync(user.Id);

        (await flow.Browser.GetAsync("/Account/Manage")).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await flow.RefreshAsync(tokens.RefreshToken!)).Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task PasswordReset_ShouldChangePasswordAndRevokeExistingSessions()
    {
        var user = await _scenario.CreateUserAsync("reset");
        var victimSession = new OidcFlow(factory.CreateBrowser(), _scenario);
        await victimSession.LoginAsync(user.UserName);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        var page = await flow.Browser.GetStringAsync("/Account/ForgotPassword");

        await flow.Browser.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = user.Email,
            ["__RequestVerificationToken"] = OidcFlow.ExtractAntiforgery(page)
        }));

        var email = factory.Emails.LastTo(user.Email);
        email.Should().NotBeNull();
        var link = WebUtility.HtmlDecode(LinkPattern().Match(email!.Value.Body).Groups["href"].Value);
        var resetPage = await flow.Browser.GetAsync(new Uri(link).PathAndQuery);
        var html = await resetPage.Content.ReadAsStringAsync();
        const string newPassword = "Another-Long-Password-2026";
        var reset = await flow.Browser.PostAsync("/Account/ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserId"] = user.Id.ToString(),
            ["Token"] = WebUtility.HtmlDecode(TokenInputPattern().Match(html).Groups["value"].Value),
            ["Password"] = newPassword,
            ["ConfirmPassword"] = newPassword,
            ["__RequestVerificationToken"] = OidcFlow.ExtractAntiforgery(html)
        }));

        reset.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await victimSession.Browser.GetAsync("/Account/Manage")).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await new OidcFlow(factory.CreateBrowser(), _scenario).LoginAsync(user.UserName)).StatusCode.Should().Be(HttpStatusCode.OK, "old password is refused");
        (await new OidcFlow(factory.CreateBrowser(), _scenario).LoginAsync(user.UserName, newPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task PasswordReset_UnknownEmail_ShouldBehaveLikeKnownEmail()
    {
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        var page = await flow.Browser.GetStringAsync("/Account/ForgotPassword");

        var response = await flow.Browser.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = $"unknown-{Guid.NewGuid():N}@example.com",
            ["__RequestVerificationToken"] = OidcFlow.ExtractAntiforgery(page)
        }));

        (await response.Content.ReadAsStringAsync()).Should().Contain("If an active account matches this address");
    }

    [Fact]
    public async Task RevokedConsent_ShouldRevokeRefreshTokensAndPromptAgain()
    {
        var organization = await _scenario.CreateOrganizationAsync("org");
        var user = await _scenario.CreateUserAsync("consent");
        await _scenario.AddMemberAsync(organization, user);
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);
        var tokens = await flow.RedeemAsync(await flow.AuthorizeAndGetCodeAsync("openid offline_access"));

        flow.ResetPkce();
        var silent = await flow.AuthorizeAsync(flow.AuthorizeUrl("openid offline_access"));
        OidcFlow.RedirectParameters(silent).Should().ContainKey("code", "consent is remembered");

        var manage = await (await flow.Browser.GetAsync("/Account/Manage")).Content.ReadAsStringAsync();
        var consentId = ConsentIdPattern().Match(manage).Groups["id"].Value;
        await flow.Browser.PostAsync("/Account/RevokeConsent", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["consentId"] = consentId,
            ["__RequestVerificationToken"] = OidcFlow.ExtractAntiforgery(manage)
        }));

        (await flow.RefreshAsync(tokens.RefreshToken!)).Error.Should().Be("invalid_grant");
        var again = await flow.AuthorizeAsync(flow.AuthorizeUrl("openid offline_access"));
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadAsStringAsync()).Should().Contain("value=\"accept\"");
    }

    [Fact]
    public async Task RefreshToken_ShouldRotateAndRebuildClaims()
    {
        var organization = await _scenario.CreateOrganizationAsync("org");
        var user = await _scenario.CreateUserAsync("refresh");
        await _scenario.AddMemberAsync(organization, user);
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);
        var tokens = await flow.RedeemAsync(await flow.AuthorizeAndGetCodeAsync("openid offline_access"));

        var refreshed = await flow.RefreshAsync(tokens.RefreshToken!);

        refreshed.Succeeded.Should().BeTrue(refreshed.Json.ToString());
        refreshed.RefreshToken.Should().NotBe(tokens.RefreshToken);
        OidcFlow.ReadJwt(refreshed.AccessToken).GetClaim("org_id").Value.Should().Be(organization.Id.ToString());
    }

    [Fact]
    public async Task SuspendedMembership_ShouldRejectRefresh()
    {
        var organization = await _scenario.CreateOrganizationAsync("org");
        var user = await _scenario.CreateUserAsync("suspended");
        await _scenario.AddMemberAsync(organization, user);
        await _scenario.CreateClientAsync(organization);
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);
        var tokens = await flow.RedeemAsync(await flow.AuthorizeAndGetCodeAsync("openid offline_access"));

        await _scenario.WithScopeAsync(async sp =>
        {
            var membership = await sp.GetRequiredService<IMembershipRepository>().GetAsync(organization.Id, user.Id);
            membership!.Suspend(DateTime.UtcNow);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            return true;
        });

        (await flow.RefreshAsync(tokens.RefreshToken!)).Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Jwks_ShouldPublishActiveAndPendingKeysWithoutPrivateMaterial()
    {
        var keys = await _scenario.WithScopeAsync(sp => sp.GetRequiredService<SigningKeyManager>().LoadKeysAsync(KeyUsage.Signing));
        var jwks = new JsonWebKeySet(await factory.CreateBrowser().GetStringAsync("/.well-known/jwks"));

        jwks.Keys.Select(k => k.Kid).Should().Contain(keys[0].KeyId);
        jwks.Keys.Should().OnlyContain(k => k.D == null && k.P == null && k.Q == null);
        jwks.Keys.Should().OnlyContain(k => k.Alg == SecurityAlgorithms.RsaSha256 || k.Alg == null);
    }

    private async Task DisableUserAsync(Guid userId)
    {
        await _scenario.WithScopeAsync(async sp =>
        {
            var user = await sp.GetRequiredService<IUserRepository>().GetByIdAsync(userId);
            user!.Disable(DateTime.UtcNow);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            await sp.GetRequiredService<IUserAuthenticationService>().RevokeEverythingAsync(userId, "test");
            return true;
        });
    }

    private static string Cookies(HttpResponseMessage response)
        => string.Join("; ", response.Headers.TryGetValues("Set-Cookie", out var values) ? values.Select(v => v.Split(';')[0]) : []);

    private static Task<HttpResponseMessage> SendWithCookieAsync(HttpClient client, HttpMethod method, string url, string cookie,
        Dictionary<string, string>? form = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", cookie);
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        return client.SendAsync(request);
    }

    [GeneratedRegex("href=\"(?<href>[^\"]+)\"")]
    private static partial Regex LinkPattern();

    [GeneratedRegex("<input type=\"hidden\"[^>]*name=\"Token\"[^>]*value=\"(?<value>[^\"]*)\"")]
    private static partial Regex TokenInputPattern();

    [GeneratedRegex("name=\"consentId\" value=\"(?<id>[0-9a-fA-F-]{36})\"")]
    private static partial Regex ConsentIdPattern();
}
