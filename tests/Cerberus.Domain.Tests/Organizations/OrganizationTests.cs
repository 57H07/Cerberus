using Cerberus.Domain.Exceptions;
using Cerberus.Domain.Organizations;

namespace Cerberus.Domain.Tests.Organizations;

public class OrganizationTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-corp-2")]
    public void Create_WithValidSlug_ShouldSucceed(string slug)
    {
        var organization = Organization.Create("Acme", slug, TestData.Now);

        organization.Slug.Should().Be(slug);
        organization.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("AC")]
    [InlineData("Acme")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("ac me")]
    public void Create_WithInvalidSlug_ShouldThrow(string slug)
    {
        var act = () => Organization.Create("Acme", slug, TestData.Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Membership_Revoked_ShouldNotBeResumable()
    {
        var membership = OrganizationMembership.Create(Guid.NewGuid(), Guid.NewGuid(), TestData.Now);
        membership.Revoke(TestData.Now);

        var act = () => membership.Resume(TestData.Now);

        act.Should().Throw<InvalidDomainOperationException>();
        membership.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Invitation_Accept_ByOtherEmail_ShouldThrow()
    {
        var invitation = Invitation.Create(Guid.NewGuid(), "bob@example.com", "hash", Guid.NewGuid(), TimeSpan.FromDays(7), TestData.Now);
        var user = TestData.User();

        var act = () => invitation.Accept(user, TestData.Now);

        act.Should().Throw<InvalidDomainOperationException>();
    }

    [Fact]
    public void Invitation_Accept_WhenExpired_ShouldThrow()
    {
        var invitation = Invitation.Create(Guid.NewGuid(), "alice@example.com", "hash", Guid.NewGuid(), TimeSpan.FromDays(7), TestData.Now);

        var act = () => invitation.Accept(TestData.User(), TestData.Now.AddDays(8));

        act.Should().Throw<InvalidDomainOperationException>();
    }

    [Fact]
    public void Invitation_Accept_Twice_ShouldThrow()
    {
        var invitation = Invitation.Create(Guid.NewGuid(), "ALICE@example.com", "hash", Guid.NewGuid(), TimeSpan.FromDays(7), TestData.Now);
        var user = TestData.User();
        invitation.Accept(user, TestData.Now);

        var act = () => invitation.Accept(user, TestData.Now);

        act.Should().Throw<InvalidDomainOperationException>();
    }
}
