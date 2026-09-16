using Cerberus.Domain.Clients;
using Cerberus.Domain.Consents;

namespace Cerberus.Domain.Tests.Consents;

public class ConsentTests
{
    private readonly OidcClient _client = TestData.Client(Guid.NewGuid(), Scopes.OpenId, Scopes.Profile, Scopes.Email);
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _organizationId = Guid.NewGuid();

    [Fact]
    public void Evaluate_NoConsent_ShouldRequireConsent()
    {
        ConsentEvaluator.Evaluate(_client, null, [Scopes.OpenId], false, TestData.Now).Should().Be(ConsentDecision.Required);
    }

    [Fact]
    public void Evaluate_RememberedConsentCoveringScopes_ShouldGrant()
    {
        var consent = Grant(Scopes.OpenId, Scopes.Profile);

        ConsentEvaluator.Evaluate(_client, consent, [Scopes.OpenId, Scopes.Profile], false, TestData.Now)
            .Should().Be(ConsentDecision.Granted);
    }

    [Fact]
    public void Evaluate_NewScopeRequested_ShouldRequireConsentAgain()
    {
        var consent = Grant(Scopes.OpenId, Scopes.Profile);

        ConsentEvaluator.Evaluate(_client, consent, [Scopes.OpenId, Scopes.Email], false, TestData.Now)
            .Should().Be(ConsentDecision.Required);
    }

    [Fact]
    public void Evaluate_RevokedConsent_ShouldRequireConsent()
    {
        var consent = Grant(Scopes.OpenId);
        consent.Revoke(TestData.Now);

        ConsentEvaluator.Evaluate(_client, consent, [Scopes.OpenId], false, TestData.Now).Should().Be(ConsentDecision.Required);
    }

    [Fact]
    public void Evaluate_ExpiredConsent_ShouldRequireConsent()
    {
        var consent = UserConsent.Grant(_userId, _client.Id, _organizationId, [Scopes.OpenId], TimeSpan.FromDays(30), TestData.Now);

        ConsentEvaluator.Evaluate(_client, consent, [Scopes.OpenId], false, TestData.Now.AddDays(31))
            .Should().Be(ConsentDecision.Required);
    }

    [Fact]
    public void Evaluate_AlwaysPromptPolicy_ShouldRequireConsentEvenWhenRemembered()
    {
        _client.SetConsentPolicy(ConsentPolicy.AlwaysPrompt, null, TestData.Now);

        ConsentEvaluator.Evaluate(_client, Grant(Scopes.OpenId), [Scopes.OpenId], false, TestData.Now)
            .Should().Be(ConsentDecision.Required);
    }

    [Fact]
    public void Evaluate_PromptConsentParameter_ShouldRequireConsent()
    {
        ConsentEvaluator.Evaluate(_client, Grant(Scopes.OpenId), [Scopes.OpenId], true, TestData.Now)
            .Should().Be(ConsentDecision.Required);
    }

    [Fact]
    public void Evaluate_TrustedClient_ShouldNeverPrompt()
    {
        _client.SetConsentPolicy(ConsentPolicy.Trusted, null, TestData.Now);

        ConsentEvaluator.Evaluate(_client, null, [Scopes.OpenId, Scopes.Email], true, TestData.Now)
            .Should().Be(ConsentDecision.Granted);
    }

    [Fact]
    public void Renew_ActiveConsent_ShouldMergeScopes()
    {
        var consent = Grant(Scopes.OpenId, Scopes.Profile);

        consent.Renew([Scopes.OpenId, Scopes.Email], null, TestData.Now);

        consent.GrantedScopes.Should().BeEquivalentTo([Scopes.OpenId, Scopes.Profile, Scopes.Email]);
    }

    [Fact]
    public void Renew_RevokedConsent_ShouldOnlyKeepNewScopes()
    {
        var consent = Grant(Scopes.OpenId, Scopes.Profile);
        consent.LinkAuthorization("auth-1");
        consent.Revoke(TestData.Now);

        consent.Renew([Scopes.OpenId], null, TestData.Now.AddMinutes(1));

        consent.GrantedScopes.Should().BeEquivalentTo([Scopes.OpenId]);
        consent.IsActive(TestData.Now.AddMinutes(1)).Should().BeTrue();
        consent.AuthorizationId.Should().BeNull();
    }

    private UserConsent Grant(params string[] scopes)
        => UserConsent.Grant(_userId, _client.Id, _organizationId, scopes, null, TestData.Now);
}
