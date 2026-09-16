using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Application.Oidc;
using Cerberus.Application.Services;
using Cerberus.Domain.Applications;
using Cerberus.Domain.Authorization;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Consents;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;
using Moq;

namespace Cerberus.Application.Tests;

public class OidcAuthorizationServiceTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IOrganizationRepository> _organizations = new();
    private readonly Mock<IMembershipRepository> _memberships = new();
    private readonly Mock<IOidcClientRepository> _clients = new();
    private readonly Mock<IClientApplicationRepository> _applications = new();
    private readonly Mock<IApiScopeRepository> _apiScopes = new();
    private readonly Mock<IRoleAssignmentRepository> _assignments = new();
    private readonly Mock<IUserConsentRepository> _consents = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IClock> _clock = new();

    private readonly User _user = User.Create("Alice", "Martin", "alice@example.com", "alice", Now);
    private readonly Organization _acme = Organization.Create("Acme", "acme", Now);
    private readonly Organization _globex = Organization.Create("Globex", "globex", Now);
    private readonly ClientApplication _application;
    private readonly OidcClient _client;

    public OidcAuthorizationServiceTests()
    {
        _application = ClientApplication.Create("CRM", null, _acme.Id, Now);
        _client = OidcClient.Create(_application.Id, "crm", "CRM", ClientType.Confidential, ["https://crm.example.com/cb"], [],
            [Scopes.OpenId, Scopes.Profile, Scopes.OrganizationRoles], Now);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _clients.Setup(r => r.GetByClientIdAsync("crm", It.IsAny<CancellationToken>())).ReturnsAsync(_client);
        _applications.Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _apiScopes.Setup(r => r.GetByNamesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _organizations.Setup(r => r.GetBySlugAsync("acme", It.IsAny<CancellationToken>())).ReturnsAsync(_acme);
        _organizations.Setup(r => r.GetBySlugAsync("globex", It.IsAny<CancellationToken>())).ReturnsAsync(_globex);
        _organizations.Setup(r => r.GetByIdAsync(_acme.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_acme);
        _assignments.Setup(r => r.ListOrganizationAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private OidcAuthorizationService CreateService() => new(
        _users.Object, _organizations.Object, _memberships.Object, _clients.Object, _applications.Object, _apiScopes.Object,
        _assignments.Object, _consents.Object, _audit.Object, _unitOfWork.Object, _clock.Object);

    private AuthorizationRequestDto Request(string? organization, params string[] scopes)
        => new(_user.Id, null, "crm", scopes.Length == 0 ? [Scopes.OpenId] : scopes, organization, false);

    [Fact]
    public async Task Evaluate_UnknownClient_ShouldBeRejected()
    {
        var result = await CreateService().EvaluateAsync(Request("acme") with { ClientId = "unknown" });

        result.Outcome.Should().Be(AuthorizationOutcome.Rejected);
        result.Error.Should().Be(OidcErrors.UnauthorizedClient);
    }

    [Fact]
    public async Task Evaluate_ScopeNotAllowedForClient_ShouldBeRejected()
    {
        var result = await CreateService().EvaluateAsync(Request("acme", Scopes.OpenId, Scopes.Email));

        result.Error.Should().Be(OidcErrors.InvalidScope);
    }

    [Fact]
    public async Task Evaluate_RequestedOrganizationWhereUserIsNotMember_ShouldBeDeniedWithGenericMessage()
    {
        var result = await CreateService().EvaluateAsync(Request("acme"));

        result.Error.Should().Be(OidcErrors.AccessDenied);
        result.ErrorDescription.Should().NotContain("member");
    }

    [Fact]
    public async Task Evaluate_MemberWithoutConsent_ShouldRequireConsentForThatOrganization()
    {
        _memberships.Setup(r => r.GetAsync(_acme.Id, _user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(OrganizationMembership.Create(_acme.Id, _user.Id, Now));

        var result = await CreateService().EvaluateAsync(Request("acme"));

        result.Outcome.Should().Be(AuthorizationOutcome.ConsentRequired);
        result.Consent!.Organization.Id.Should().Be(_acme.Id);
    }

    [Fact]
    public async Task Evaluate_ConsentGivenForAnotherOrganization_ShouldNotApply()
    {
        _application.GrantOrganization(_globex.Id, Now);
        _memberships.Setup(r => r.GetAsync(_globex.Id, _user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(OrganizationMembership.Create(_globex.Id, _user.Id, Now));
        _consents.Setup(r => r.GetAsync(_user.Id, _client.Id, _acme.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserConsent.Grant(_user.Id, _client.Id, _acme.Id, [Scopes.OpenId], null, Now));

        var result = await CreateService().EvaluateAsync(Request("globex"));

        result.Outcome.Should().Be(AuthorizationOutcome.ConsentRequired);
    }

    [Fact]
    public async Task Evaluate_SeveralEligibleOrganizations_ShouldAskForSelection()
    {
        _application.GrantOrganization(_globex.Id, Now);
        _memberships.Setup(r => r.ListByUserAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            OrganizationMembership.Create(_acme.Id, _user.Id, Now),
            OrganizationMembership.Create(_globex.Id, _user.Id, Now)
        ]);
        _organizations.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([_acme, _globex]);

        var result = await CreateService().EvaluateAsync(Request(null));

        result.Outcome.Should().Be(AuthorizationOutcome.OrganizationSelectionRequired);
        result.OrganizationOptions!.Select(o => o.Slug).Should().BeEquivalentTo(["acme", "globex"]);
    }

    [Fact]
    public async Task Evaluate_SingleEligibleOrganizationAndTrustedClient_ShouldGrantWithOrganizationClaims()
    {
        _client.SetConsentPolicy(ConsentPolicy.Trusted, null, Now);
        _memberships.Setup(r => r.ListByUserAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            OrganizationMembership.Create(_acme.Id, _user.Id, Now),
            OrganizationMembership.Create(_globex.Id, _user.Id, Now)
        ]);
        _organizations.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([_acme, _globex]);

        var result = await CreateService().EvaluateAsync(Request(null));

        result.Outcome.Should().Be(AuthorizationOutcome.Granted);
        result.Subject!.OrganizationId.Should().Be(_acme.Id, "globex is not granted to the application");
        result.Subject.Claims.Should().Contain(c => c.Type == CerberusClaimTypes.OrganizationId && c.Value == _acme.Id.ToString());
    }

    [Fact]
    public async Task Revalidate_AfterConsentRevoked_ShouldReturnInvalidGrant()
    {
        _memberships.Setup(r => r.GetAsync(_acme.Id, _user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(OrganizationMembership.Create(_acme.Id, _user.Id, Now));
        var consent = UserConsent.Grant(_user.Id, _client.Id, _acme.Id, [Scopes.OpenId], null, Now);
        consent.Revoke(Now);
        _consents.Setup(r => r.GetAsync(_user.Id, _client.Id, _acme.Id, It.IsAny<CancellationToken>())).ReturnsAsync(consent);

        var result = await CreateService().RevalidateAsync(_user.Id, "crm", _acme.Id, [Scopes.OpenId], null);

        result.Error.Should().Be(OidcErrors.InvalidGrant);
    }

    [Fact]
    public async Task Revalidate_WithValidContext_ShouldRebuildRolesFromCurrentData()
    {
        _memberships.Setup(r => r.GetAsync(_acme.Id, _user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(OrganizationMembership.Create(_acme.Id, _user.Id, Now));
        _consents.Setup(r => r.GetAsync(_user.Id, _client.Id, _acme.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserConsent.Grant(_user.Id, _client.Id, _acme.Id, [Scopes.OpenId, Scopes.OrganizationRoles], null, Now));
        _assignments.Setup(r => r.ListOrganizationAssignmentsAsync(_acme.Id, _user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RoleAssignmentView(Guid.NewGuid(), _user.Id, Guid.NewGuid(), "Sales", RoleScope.Organization, _acme.Id)]);

        var result = await CreateService().RevalidateAsync(_user.Id, "crm", _acme.Id, [Scopes.OpenId, Scopes.OrganizationRoles], null);

        result.Outcome.Should().Be(AuthorizationOutcome.Granted);
        result.Subject!.Claims.Should().Contain(c => c.Type == CerberusClaimTypes.OrganizationRoles && c.Value == "Sales");
    }

    [Fact]
    public async Task GrantConsent_ShouldStoreConsentScopedToOrganization()
    {
        _memberships.Setup(r => r.GetAsync(_acme.Id, _user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(OrganizationMembership.Create(_acme.Id, _user.Id, Now));
        UserConsent? stored = null;
        _consents.Setup(r => r.Add(It.IsAny<UserConsent>())).Callback<UserConsent>(c => stored = c);

        var result = await CreateService().GrantConsentAsync(Request("acme", Scopes.OpenId, Scopes.Profile));

        result.Outcome.Should().Be(AuthorizationOutcome.Granted);
        result.Subject!.IsRememberedConsent.Should().BeTrue();
        stored!.OrganizationId.Should().Be(_acme.Id);
        stored.GrantedScopes.Should().BeEquivalentTo([Scopes.OpenId, Scopes.Profile]);
    }
}
