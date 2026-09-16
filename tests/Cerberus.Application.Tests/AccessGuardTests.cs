using Cerberus.Application.Authorization;
using Cerberus.Application.Exceptions;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Application.Services;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;
using Moq;

namespace Cerberus.Application.Tests;

public class AccessGuardTests
{
    private readonly Mock<IActorContext> _actor = new();
    private readonly Mock<IRoleAssignmentRepository> _assignments = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _organizationId = Guid.NewGuid();

    private AccessGuard CreateGuard() => new(_actor.Object, _assignments.Object, _audit.Object, _unitOfWork.Object);

    [Fact]
    public async Task RequireOrganizationPermission_Anonymous_ShouldThrow()
    {
        var act = () => CreateGuard().RequireOrganizationPermissionAsync(_organizationId, PermissionCatalog.OrganizationMembersRead);

        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task RequireOrganizationPermission_WithPermission_ShouldReturnActor()
    {
        _actor.SetupGet(a => a.UserId).Returns(_userId);
        _assignments.Setup(a => a.GetOrganizationPermissionsAsync(_userId, _organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { PermissionCatalog.OrganizationMembersRead });

        var result = await CreateGuard().RequireOrganizationPermissionAsync(_organizationId, PermissionCatalog.OrganizationMembersRead);

        result.Should().Be(_userId);
    }

    [Fact]
    public async Task RequireOrganizationPermission_WithoutPermission_ShouldAuditAndThrow()
    {
        _actor.SetupGet(a => a.UserId).Returns(_userId);
        _assignments.Setup(a => a.GetOrganizationPermissionsAsync(_userId, _organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        var act = () => CreateGuard().RequireOrganizationPermissionAsync(_organizationId, PermissionCatalog.OrganizationMembersManage);

        await act.Should().ThrowAsync<ForbiddenAccessException>();
        _audit.Verify(a => a.Write(AuditActions.CrossTenantAccessDenied, AuditOutcome.Denied, _organizationId, null, null, It.IsAny<object>(), null), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequirePlatformPermission_ShouldNotBeSatisfiedByOrganizationPermissions()
    {
        _actor.SetupGet(a => a.UserId).Returns(_userId);
        _assignments.Setup(a => a.GetPlatformPermissionsAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<string>());
        _assignments.Setup(a => a.GetOrganizationPermissionsAsync(_userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionCatalog.CodesFor(RoleScope.Organization).ToHashSet());

        var act = () => CreateGuard().RequirePlatformPermissionAsync(PermissionCatalog.PlatformUsersManage);

        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task RequirePermission_WithWrongScope_ShouldBeAProgrammingError()
    {
        _actor.SetupGet(a => a.UserId).Returns(_userId);

        var act = () => CreateGuard().RequirePlatformPermissionAsync(PermissionCatalog.OrganizationMembersRead);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(new[] { "org.members.read" }, new[] { "org.members.read" }, true)]
    [InlineData(new[] { "org.members.read", "org.roles.manage" }, new[] { "org.members.read" }, true)]
    [InlineData(new[] { "org.roles.manage" }, new[] { "org.members.manage" }, false)]
    [InlineData(new string[0], new[] { "org.members.read" }, false)]
    public void PrivilegeEscalationPolicy_CanGrant_ShouldRequireSubsetOfActorPermissions(string[] actor, string[] role, bool expected)
    {
        PrivilegeEscalationPolicy.CanGrant(actor.ToHashSet(), role).Should().Be(expected);
    }

    [Fact]
    public void PrivilegeEscalationPolicy_ShouldForbidSelfManagement()
    {
        PrivilegeEscalationPolicy.CanManageAssignmentsOf(_userId, _userId).Should().BeFalse();
        PrivilegeEscalationPolicy.CanManageAssignmentsOf(_userId, Guid.NewGuid()).Should().BeTrue();
    }
}
