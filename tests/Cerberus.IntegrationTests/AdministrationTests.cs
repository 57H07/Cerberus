using Cerberus.Application.Common;
using System.Net;
using System.Text.RegularExpressions;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;
using Cerberus.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Cerberus.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public partial class AdministrationTests(CerberusWebFactory factory)
{
    private readonly TestScenario _scenario = new(factory);

    [Fact]
    public async Task OrganizationAdministrator_CannotReadOrModifyAnotherOrganization()
    {
        var acme = await _scenario.CreateOrganizationAsync("acme");
        var globex = await _scenario.CreateOrganizationAsync("globex");
        var admin = await _scenario.CreateUserAsync("acme-admin");
        var globexMember = await _scenario.CreateUserAsync("globex-member");
        await _scenario.AddMemberAsync(acme, admin, Role.OrganizationAdministratorName);
        await _scenario.AddMemberAsync(globex, globexMember);
        var browser = await SignedInAsync(admin);

        (await browser.GetAsync($"/OrgAdmin/{acme.Id}/Members")).StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await browser.GetAsync($"/OrgAdmin/{globex.Id}/Members");
        read.Headers.Location!.OriginalString.Should().Be("/Account/AccessDenied");

        var suspend = await PostAsync(browser, $"/OrgAdmin/{globex.Id}/Members/Suspend/{globexMember.Id}");
        suspend.Headers.Location!.OriginalString.Should().Be("/Account/AccessDenied");
        (await MembershipStatusAsync(globex.Id, globexMember.Id)).Should().Be(Domain.Organizations.MembershipStatus.Active);

        var denied = await _scenario.WithScopeAsync(sp => sp.GetRequiredService<IAuditRepository>()
            .GetPagedAsync(new AuditFilter { OrganizationId = globex.Id, ActionPrefix = AuditActions.CrossTenantAccessDenied }));
        denied.Items.Should().Contain(e => e.ActorUserId == admin.Id && e.Outcome == AuditOutcome.Denied);
    }

    [Fact]
    public async Task OrganizationAdministrator_CannotUseAnotherOrganizationsIdentifiersThroughOwnRoute()
    {
        var acme = await _scenario.CreateOrganizationAsync("acme");
        var globex = await _scenario.CreateOrganizationAsync("globex");
        var admin = await _scenario.CreateUserAsync("acme-admin");
        var acmeMember = await _scenario.CreateUserAsync("acme-member");
        var globexMember = await _scenario.CreateUserAsync("globex-member");
        await _scenario.AddMemberAsync(acme, admin, Role.OrganizationAdministratorName);
        await _scenario.AddMemberAsync(acme, acmeMember);
        await _scenario.AddMemberAsync(globex, globexMember);
        var globexRole = await _scenario.CreateOrganizationRoleAsync(globex, "Globex auditors");
        var browser = await SignedInAsync(admin);

        await PostAsync(browser, $"/OrgAdmin/{acme.Id}/Members/AssignRole/{acmeMember.Id}", new() { ["roleId"] = globexRole.Id.ToString() });
        await PostAsync(browser, $"/OrgAdmin/{acme.Id}/Members/Suspend/{globexMember.Id}");

        var assignments = await _scenario.WithScopeAsync(sp => sp.GetRequiredService<IRoleAssignmentRepository>().ListOrganizationAssignmentsAsync(acme.Id, acmeMember.Id));
        assignments.Should().BeEmpty();
        (await MembershipStatusAsync(globex.Id, globexMember.Id)).Should().Be(Domain.Organizations.MembershipStatus.Active);
    }

    [Fact]
    public async Task RoleManager_CannotGrantPermissionsHeDoesNotHold()
    {
        var acme = await _scenario.CreateOrganizationAsync("acme");
        var manager = await _scenario.CreateUserAsync("manager");
        var member = await _scenario.CreateUserAsync("member");
        await _scenario.CreateOrganizationRoleAsync(acme, "Role managers", PermissionCatalog.OrganizationRolesManage, PermissionCatalog.OrganizationMembersRead);
        await _scenario.AddMemberAsync(acme, manager, "Role managers");
        await _scenario.AddMemberAsync(acme, member);
        var adminRole = await _scenario.WithScopeAsync(sp => sp.GetRequiredService<IRoleRepository>().FindOrganizationRoleByNameAsync(acme.Id, Role.OrganizationAdministratorName));
        var browser = await SignedInAsync(manager);

        var createRole = await PostAsync(browser, $"/OrgAdmin/{acme.Id}/Roles/Create", new()
        {
            ["Name"] = "Super",
            ["Permissions"] = PermissionCatalog.OrganizationMembersManage
        });
        var assignAdmin = await PostAsync(browser, $"/OrgAdmin/{acme.Id}/Members/AssignRole/{member.Id}", new() { ["roleId"] = adminRole!.Id.ToString() });
        var selfPromotion = await PostAsync(browser, $"/OrgAdmin/{acme.Id}/Members/AssignRole/{manager.Id}", new() { ["roleId"] = adminRole.Id.ToString() });

        createRole.Headers.Location!.OriginalString.Should().Be("/Account/AccessDenied");
        assignAdmin.Headers.Location!.OriginalString.Should().Be("/Account/AccessDenied");
        selfPromotion.Headers.Location!.OriginalString.Should().Be("/Account/AccessDenied");
        (await _scenario.WithScopeAsync(sp => sp.GetRequiredService<IRoleAssignmentRepository>().GetOrganizationPermissionsAsync(member.Id, acme.Id)))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task RoleManager_CanAssignRoleWithinHisOwnPermissions()
    {
        var acme = await _scenario.CreateOrganizationAsync("acme");
        var manager = await _scenario.CreateUserAsync("manager");
        var member = await _scenario.CreateUserAsync("member");
        await _scenario.CreateOrganizationRoleAsync(acme, "Role managers", PermissionCatalog.OrganizationRolesManage, PermissionCatalog.OrganizationMembersRead);
        var readers = await _scenario.CreateOrganizationRoleAsync(acme, "Readers", PermissionCatalog.OrganizationMembersRead);
        await _scenario.AddMemberAsync(acme, manager, "Role managers");
        await _scenario.AddMemberAsync(acme, member);
        var browser = await SignedInAsync(manager);

        var response = await PostAsync(browser, $"/OrgAdmin/{acme.Id}/Members/AssignRole/{member.Id}", new() { ["roleId"] = readers.Id.ToString() });

        response.Headers.Location!.OriginalString.Should().Be($"/OrgAdmin/{acme.Id}/Members");
        (await _scenario.WithScopeAsync(sp => sp.GetRequiredService<IRoleAssignmentRepository>().GetOrganizationPermissionsAsync(member.Id, acme.Id)))
            .Should().BeEquivalentTo([PermissionCatalog.OrganizationMembersRead]);
    }

    [Fact]
    public async Task OrganizationAdministrator_CannotAccessPlatformAdministration()
    {
        var acme = await _scenario.CreateOrganizationAsync("acme");
        var admin = await _scenario.CreateUserAsync("acme-admin");
        await _scenario.AddMemberAsync(acme, admin, Role.OrganizationAdministratorName);
        var browser = await SignedInAsync(admin);

        foreach (var url in new[] { "/Admin/Users", "/Admin/Organizations", "/Admin/Applications", "/Admin/Roles", "/Admin/Audit" })
        {
            (await browser.GetAsync(url)).Headers.Location!.OriginalString.Should().Be("/Account/AccessDenied", url);
        }
    }

    [Fact]
    public async Task Anonymous_ShouldBeRedirectedToLogin()
    {
        var response = await factory.CreateBrowser().GetAsync("/Admin/Users");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be("/Account/Login");
    }

    [Fact]
    public async Task PlatformAdministrator_CreatesClient_SecretShownOnce_ThenDisablingClientBlocksTokens()
    {
        var owner = await _scenario.CreateOrganizationAsync("owner");
        var platformAdmin = await _scenario.CreateUserAsync("platform");
        await _scenario.GrantPlatformAdministratorAsync(platformAdmin);
        var user = await _scenario.CreateUserAsync("user");
        await _scenario.AddMemberAsync(owner, user);
        var (application, _) = await _scenario.CreateClientAsync(owner);
        var admin = await SignedInAsync(platformAdmin);
        var clientId = _scenario.Name("created");

        var created = await PostAsync(admin, $"/Admin/Applications/CreateClient/{application.Id}", new()
        {
            ["ApplicationId"] = application.Id.ToString(),
            ["ClientId"] = clientId,
            ["DisplayName"] = "Created by admin",
            ["ClientType"] = "Confidential",
            ["ConsentPolicy"] = "Trusted",
            ["RedirectUris"] = "https://created.example.com/cb",
            ["AllowedScopes"] = "openid"
        });
        var html = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("will never be displayed again");
        var details = await (await admin.GetAsync($"/Admin/Applications/Details/{application.Id}")).Content.ReadAsStringAsync();
        details.Should().Contain(clientId);
        SecretPattern().Match(details).Success.Should().BeFalse("the secret is never rendered again");

        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        await flow.LoginAsync(user.UserName);
        var tokens = await flow.RedeemAsync(await flow.AuthorizeAndGetCodeAsync("openid offline_access"));
        var clientEntityId = (await _scenario.WithScopeAsync(sp => sp.GetRequiredService<IOidcClientRepository>().GetByClientIdAsync(_scenario.ClientId)))!.Id;

        await PostAsync(admin, $"/Admin/Applications/SetClientActive/{clientEntityId}", new() { ["active"] = "false" });

        (await flow.RefreshAsync(tokens.RefreshToken!)).Succeeded.Should().BeFalse();
        flow.ResetPkce();
        var authorize = await flow.Browser.GetAsync(flow.AuthorizeUrl());
        authorize.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a disabled client is rejected without redirecting to it");
        (await authorize.Content.ReadAsStringAsync()).Should().Contain("unauthorized_client");
    }

    [Fact]
    public async Task Invitation_NewUser_ShouldRegisterAndBecomeMember()
    {
        var acme = await _scenario.CreateOrganizationAsync("acme");
        var admin = await _scenario.CreateUserAsync("acme-admin");
        await _scenario.AddMemberAsync(acme, admin, Role.OrganizationAdministratorName);
        var browser = await SignedInAsync(admin);
        var email = $"{_scenario.Name("invitee")}@example.com";

        await PostAsync(browser, $"/OrgAdmin/{acme.Id}/Members/Invite", new() { ["email"] = email });

        var message = factory.Emails.LastTo(email);
        message.Should().NotBeNull();
        var link = new Uri(WebUtility.HtmlDecode(LinkPattern().Match(message!.Value.Body).Groups["href"].Value));
        var token = System.Web.HttpUtility.ParseQueryString(link.Query)["token"]!;
        var visitor = factory.CreateBrowser();
        var registerPage = await (await visitor.GetAsync($"/Account/Register?token={Uri.EscapeDataString(token)}")).Content.ReadAsStringAsync();
        var register = await visitor.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Token"] = token,
            ["FirstName"] = "Ivy",
            ["LastName"] = "Invitee",
            ["UserName"] = _scenario.Name("ivy"),
            ["Password"] = TestScenario.Password,
            ["ConfirmPassword"] = TestScenario.Password,
            ["__RequestVerificationToken"] = OidcFlow.ExtractAntiforgery(registerPage)
        }));

        register.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var members = await (await browser.GetAsync($"/OrgAdmin/{acme.Id}/Members")).Content.ReadAsStringAsync();
        members.Should().Contain(email);
        var reuse = await (await factory.CreateBrowser().GetAsync($"/Account/Invitation?token={Uri.EscapeDataString(token)}")).Content.ReadAsStringAsync();
        reuse.Should().Contain("Invalid invitation");
    }

    [Fact]
    public async Task Responses_ShouldCarrySecurityHeaders()
    {
        var response = await factory.CreateBrowser().GetAsync("/Account/Login");

        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("frame-ancestors 'none'");
    }

    private async Task<HttpClient> SignedInAsync(Domain.Users.User user)
    {
        var flow = new OidcFlow(factory.CreateBrowser(), _scenario);
        (await flow.LoginAsync(user.UserName)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        return flow.Browser;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient browser, string url, Dictionary<string, string>? fields = null)
    {
        var page = await (await browser.GetAsync("/Account/Manage")).Content.ReadAsStringAsync();
        var form = new Dictionary<string, string>(fields ?? []) { ["__RequestVerificationToken"] = OidcFlow.ExtractAntiforgery(page) };
        return await browser.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private Task<Domain.Organizations.MembershipStatus> MembershipStatusAsync(Guid organizationId, Guid userId)
        => _scenario.WithScopeAsync(async sp => (await sp.GetRequiredService<IMembershipRepository>().GetAsync(organizationId, userId))!.Status);

    [GeneratedRegex("href=\"(?<href>[^\"]+)\"")]
    private static partial Regex LinkPattern();

    [GeneratedRegex("user-select-all")]
    private static partial Regex SecretPattern();
}
