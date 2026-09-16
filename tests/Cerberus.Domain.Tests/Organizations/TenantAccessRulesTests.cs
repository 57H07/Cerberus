using Cerberus.Domain.Organizations;

namespace Cerberus.Domain.Tests.Organizations;

public class TenantAccessRulesTests
{
    private readonly Domain.Users.User _user = TestData.User();
    private readonly Organization _acme = TestData.Organization("acme");
    private readonly Organization _globex = TestData.Organization("globex");

    [Fact]
    public void Evaluate_MemberOfOwnerOrganization_ShouldSucceed()
    {
        var application = TestData.Application(_acme.Id);
        var client = TestData.Client(application.Id);
        var membership = OrganizationMembership.Create(_acme.Id, _user.Id, TestData.Now);

        TenantAccessRules.Evaluate(_user, _acme, membership, client, application).Should().Be(TenantAccessFailure.None);
    }

    [Fact]
    public void Evaluate_UserInSeveralOrganizations_ShouldOnlyAllowGrantedOnes()
    {
        var application = TestData.Application(_acme.Id);
        var client = TestData.Client(application.Id);
        var acmeMembership = OrganizationMembership.Create(_acme.Id, _user.Id, TestData.Now);
        var globexMembership = OrganizationMembership.Create(_globex.Id, _user.Id, TestData.Now);

        TenantAccessRules.Evaluate(_user, _acme, acmeMembership, client, application).Should().Be(TenantAccessFailure.None);
        TenantAccessRules.Evaluate(_user, _globex, globexMembership, client, application)
            .Should().Be(TenantAccessFailure.ApplicationNotAvailableForOrganization);

        application.GrantOrganization(_globex.Id, TestData.Now);

        TenantAccessRules.Evaluate(_user, _globex, globexMembership, client, application).Should().Be(TenantAccessFailure.None);
    }

    [Fact]
    public void Evaluate_UnknownOrganization_ShouldFail()
    {
        var application = TestData.Application(_acme.Id);

        TenantAccessRules.Evaluate(_user, null, null, TestData.Client(application.Id), application)
            .Should().Be(TenantAccessFailure.OrganizationNotFound);
    }

    [Fact]
    public void Evaluate_SuspendedOrganization_ShouldFail()
    {
        var application = TestData.Application(_acme.Id);
        var membership = OrganizationMembership.Create(_acme.Id, _user.Id, TestData.Now);
        _acme.Suspend(TestData.Now);

        TenantAccessRules.Evaluate(_user, _acme, membership, TestData.Client(application.Id), application)
            .Should().Be(TenantAccessFailure.OrganizationSuspended);
    }

    [Fact]
    public void Evaluate_NonMember_ShouldFail()
    {
        var application = TestData.Application(_acme.Id);

        TenantAccessRules.Evaluate(_user, _acme, null, TestData.Client(application.Id), application)
            .Should().Be(TenantAccessFailure.NotAMember);
    }

    [Fact]
    public void Evaluate_MembershipOfAnotherOrganization_ShouldFail()
    {
        var application = TestData.Application(_acme.Id);
        var globexMembership = OrganizationMembership.Create(_globex.Id, _user.Id, TestData.Now);

        TenantAccessRules.Evaluate(_user, _acme, globexMembership, TestData.Client(application.Id), application)
            .Should().Be(TenantAccessFailure.NotAMember);
    }

    [Fact]
    public void Evaluate_SuspendedMembership_ShouldFail()
    {
        var application = TestData.Application(_acme.Id);
        var membership = OrganizationMembership.Create(_acme.Id, _user.Id, TestData.Now);
        membership.Suspend(TestData.Now);

        TenantAccessRules.Evaluate(_user, _acme, membership, TestData.Client(application.Id), application)
            .Should().Be(TenantAccessFailure.MembershipInactive);
    }

    [Fact]
    public void Evaluate_DisabledUser_ShouldFail()
    {
        var application = TestData.Application(_acme.Id);
        var membership = OrganizationMembership.Create(_acme.Id, _user.Id, TestData.Now);
        _user.Disable(TestData.Now);

        TenantAccessRules.Evaluate(_user, _acme, membership, TestData.Client(application.Id), application)
            .Should().Be(TenantAccessFailure.UserDisabled);
    }

    [Fact]
    public void Evaluate_DisabledClient_ShouldFail()
    {
        var application = TestData.Application(_acme.Id);
        var client = TestData.Client(application.Id);
        var membership = OrganizationMembership.Create(_acme.Id, _user.Id, TestData.Now);
        client.Disable(TestData.Now);

        TenantAccessRules.Evaluate(_user, _acme, membership, client, application).Should().Be(TenantAccessFailure.ClientDisabled);
    }

    [Fact]
    public void Evaluate_AccessSuspendedByOrganization_ShouldFail()
    {
        var application = TestData.Application(_acme.Id);
        var membership = OrganizationMembership.Create(_acme.Id, _user.Id, TestData.Now);
        application.SetOrganizationAccessEnabled(_acme.Id, false, TestData.Now);

        TenantAccessRules.Evaluate(_user, _acme, membership, TestData.Client(application.Id), application)
            .Should().Be(TenantAccessFailure.ApplicationNotAvailableForOrganization);
    }
}
