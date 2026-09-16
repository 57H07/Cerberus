using Cerberus.Domain.Exceptions;
using Cerberus.Domain.Users;

namespace Cerberus.Domain.Tests.Users;

public class UserTests
{
    [Fact]
    public void Create_WithValidData_ShouldNormalizeLoginAndEmail()
    {
        var user = User.Create(" Alice ", "Martin", "  Alice.Martin@Example.COM ", "Alice.M", TestData.Now);

        user.FirstName.Should().Be("Alice");
        user.Email.Should().Be("Alice.Martin@Example.COM");
        user.NormalizedEmail.Should().Be("ALICE.MARTIN@EXAMPLE.COM");
        user.NormalizedUserName.Should().Be("ALICE.M");
        user.Status.Should().Be(UserStatus.Active);
        user.EmailConfirmed.Should().BeFalse();
        user.Id.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("alice martin")]
    [InlineData("alice@corp")]
    [InlineData("")]
    public void Create_WithInvalidLogin_ShouldThrow(string login)
    {
        var act = () => User.Create("Alice", "Martin", "alice@example.com", login, TestData.Now);

        act.Should().Throw<DomainValidationException>().Which.FieldName.Should().Be(nameof(User.UserName));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a b@example.com")]
    public void Create_WithInvalidEmail_ShouldThrow(string email)
    {
        var act = () => User.Create("Alice", "Martin", email, "alice", TestData.Now);

        act.Should().Throw<DomainValidationException>().Which.FieldName.Should().Be(nameof(User.Email));
    }

    [Fact]
    public void Create_WithNonUtcDate_ShouldThrow()
    {
        var act = () => User.Create("Alice", "Martin", "alice@example.com", "alice", DateTime.Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Normalization_ShouldBeCaseInsensitive()
    {
        var lower = User.Create("A", "B", "alice@example.com", "alice", TestData.Now);
        var upper = User.Create("A", "B", "ALICE@EXAMPLE.COM", "ALICE", TestData.Now);

        lower.NormalizedEmail.Should().Be(upper.NormalizedEmail);
        lower.NormalizedUserName.Should().Be(upper.NormalizedUserName);
    }

    [Fact]
    public void Normalization_ShouldFoldCompatibilityCharacters()
    {
        Common.IdentityNormalizer.Normalize("ａlice@example.com").Should().Be("ALICE@EXAMPLE.COM");
    }

    [Fact]
    public void ChangeEmail_WithDifferentAddress_ShouldResetConfirmationAndRotateStamp()
    {
        var user = TestData.User();
        user.ConfirmEmail(TestData.Now);
        var stamp = user.SecurityStamp;

        user.ChangeEmail("new@example.com", TestData.Now);

        user.EmailConfirmed.Should().BeFalse();
        user.SecurityStamp.Should().NotBe(stamp);
    }

    [Fact]
    public void ChangeEmail_WithSameAddressDifferentCase_ShouldKeepConfirmation()
    {
        var user = TestData.User();
        user.ConfirmEmail(TestData.Now);

        user.ChangeEmail("ALICE@example.com", TestData.Now);

        user.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public void Disable_ShouldRotateSecurityStampAndDeactivate()
    {
        var user = TestData.User();
        var stamp = user.SecurityStamp;

        user.Disable(TestData.Now);

        user.IsActive.Should().BeFalse();
        user.SecurityStamp.Should().NotBe(stamp);
    }

    [Fact]
    public void Enable_ShouldClearLockout()
    {
        var user = TestData.User();
        user.RecordFailedAccess();
        user.SetLockoutEnd(TestData.Now.AddMinutes(10));
        user.Disable(TestData.Now);

        user.Enable(TestData.Now);

        user.IsActive.Should().BeTrue();
        user.AccessFailedCount.Should().Be(0);
        user.IsLockedOut(TestData.Now).Should().BeFalse();
    }
}
