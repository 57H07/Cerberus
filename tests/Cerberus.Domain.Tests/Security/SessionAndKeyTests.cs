using Cerberus.Domain.Exceptions;
using Cerberus.Domain.Keys;
using Cerberus.Domain.Sessions;

namespace Cerberus.Domain.Tests.Security;

public class SessionAndKeyTests
{
    [Fact]
    public void Session_Refresh_ShouldNeverExceedAbsoluteExpiration()
    {
        var session = UserSession.Start(Guid.NewGuid(), TimeSpan.FromHours(8), TimeSpan.FromHours(10), null, null, TestData.Now);

        session.Refresh(TimeSpan.FromHours(8), TestData.Now.AddHours(7));

        session.ExpiresAt.Should().Be(TestData.Now.AddHours(10));
    }

    [Fact]
    public void Session_Revoked_ShouldBeInactiveAndNotRefreshable()
    {
        var session = UserSession.Start(Guid.NewGuid(), TimeSpan.FromHours(1), TimeSpan.FromHours(8), null, null, TestData.Now);

        session.Revoke("logout", TestData.Now);

        session.IsActive(TestData.Now).Should().BeFalse();
        var act = () => session.Refresh(TimeSpan.FromHours(1), TestData.Now);
        act.Should().Throw<InvalidDomainOperationException>();
    }

    [Fact]
    public void Session_Expired_ShouldBeInactive()
    {
        var session = UserSession.Start(Guid.NewGuid(), TimeSpan.FromHours(1), TimeSpan.FromHours(8), null, null, TestData.Now);

        session.IsActive(TestData.Now.AddHours(2)).Should().BeFalse();
    }

    [Fact]
    public void SigningKey_ShouldGoThroughLifecycle()
    {
        var key = SigningKey.Create("kid", KeyUsage.Signing, "RS256", "protected", TestData.Now.AddDays(7), TimeSpan.FromDays(90), TimeSpan.FromDays(30), TestData.Now);

        key.GetState(TestData.Now).Should().Be(KeyState.Pending);
        key.GetState(TestData.Now.AddDays(8)).Should().Be(KeyState.Active);
        key.GetState(TestData.Now.AddDays(98)).Should().Be(KeyState.Retired);
        key.GetState(TestData.Now.AddDays(128)).Should().Be(KeyState.Expired);
    }

    [Fact]
    public void SigningKey_Revoke_ShouldExpireImmediately()
    {
        var key = SigningKey.Create("kid", KeyUsage.Signing, "RS256", "protected", TestData.Now, TimeSpan.FromDays(90), TimeSpan.FromDays(30), TestData.Now);

        key.Revoke(TestData.Now.AddDays(1));

        key.GetState(TestData.Now.AddDays(1)).Should().Be(KeyState.Expired);
    }
}
