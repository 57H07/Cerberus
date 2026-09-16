using Cerberus.Domain.Authorization;
using Cerberus.Domain.Exceptions;
using Cerberus.Domain.Organizations;

namespace Cerberus.Domain.Tests.Authorization;

public class RoleTests
{
    [Fact]
    public void OrganizationRole_WithPlatformPermission_ShouldThrow()
    {
        var role = Role.CreateOrganizationRole(Guid.NewGuid(), "Support", null, false, TestData.Now);

        var act = () => role.SetPermissions([PermissionCatalog.PlatformUsersManage], TestData.Now);

        act.Should().Throw<DomainValidationException>();
        role.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void PlatformRole_WithOrganizationPermission_ShouldThrow()
    {
        var role = Role.CreatePlatformRole("Auditor", null, false, TestData.Now);

        var act = () => role.SetPermissions([PermissionCatalog.OrganizationMembersManage], TestData.Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void SetPermissions_UnknownPermission_ShouldThrow()
    {
        var role = Role.CreatePlatformRole("Auditor", null, false, TestData.Now);

        var act = () => role.SetPermissions(["platform.everything"], TestData.Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void SetPermissions_ShouldReplaceExistingSet()
    {
        var role = Role.CreatePlatformRole("Auditor", null, false, TestData.Now);
        role.SetPermissions([PermissionCatalog.PlatformAuditRead, PermissionCatalog.PlatformUsersManage], TestData.Now);

        role.SetPermissions([PermissionCatalog.PlatformAuditRead], TestData.Now);

        role.PermissionCodes.Should().BeEquivalentTo([PermissionCatalog.PlatformAuditRead]);
    }

    [Fact]
    public void SystemRole_ShouldNotBeModifiable()
    {
        var role = Role.CreatePlatformRole(Role.PlatformAdministratorName, null, true, TestData.Now);

        var rename = () => role.Update("Other", null, TestData.Now);
        var permissions = () => role.SetPermissions([], TestData.Now);
        var delete = () => role.EnsureDeletable();

        rename.Should().Throw<InvalidDomainOperationException>();
        permissions.Should().Throw<InvalidDomainOperationException>();
        delete.Should().Throw<InvalidDomainOperationException>();
    }

    [Fact]
    public void AssignOrganizationRole_ToMemberOfAnotherOrganization_ShouldThrow()
    {
        var role = Role.CreateOrganizationRole(Guid.NewGuid(), "Support", null, false, TestData.Now);
        var membership = OrganizationMembership.Create(Guid.NewGuid(), Guid.NewGuid(), TestData.Now);

        var act = () => UserRoleAssignment.ForOrganization(membership, role, null, TestData.Now);

        act.Should().Throw<InvalidDomainOperationException>();
    }

    [Fact]
    public void AssignOrganizationRole_ToSuspendedMember_ShouldThrow()
    {
        var organizationId = Guid.NewGuid();
        var role = Role.CreateOrganizationRole(organizationId, "Support", null, false, TestData.Now);
        var membership = OrganizationMembership.Create(organizationId, Guid.NewGuid(), TestData.Now);
        membership.Suspend(TestData.Now);

        var act = () => UserRoleAssignment.ForOrganization(membership, role, null, TestData.Now);

        act.Should().Throw<InvalidDomainOperationException>();
    }

    [Fact]
    public void AssignOrganizationRole_ToActiveMember_ShouldCarryOrganization()
    {
        var organizationId = Guid.NewGuid();
        var role = Role.CreateOrganizationRole(organizationId, "Support", null, false, TestData.Now);
        var membership = OrganizationMembership.Create(organizationId, Guid.NewGuid(), TestData.Now);

        var assignment = UserRoleAssignment.ForOrganization(membership, role, null, TestData.Now);

        assignment.OrganizationId.Should().Be(organizationId);
        assignment.UserId.Should().Be(membership.UserId);
    }

    [Fact]
    public void AssignPlatformRole_WithOrganizationRole_ShouldThrow()
    {
        var role = Role.CreateOrganizationRole(Guid.NewGuid(), "Support", null, false, TestData.Now);

        var act = () => UserRoleAssignment.ForPlatform(Guid.NewGuid(), role, null, TestData.Now);

        act.Should().Throw<InvalidDomainOperationException>();
    }

    [Fact]
    public void AssignPlatformRole_WithinOrganization_ShouldThrow()
    {
        var role = Role.CreatePlatformRole("Auditor", null, false, TestData.Now);
        var membership = OrganizationMembership.Create(Guid.NewGuid(), Guid.NewGuid(), TestData.Now);

        var act = () => UserRoleAssignment.ForOrganization(membership, role, null, TestData.Now);

        act.Should().Throw<InvalidDomainOperationException>();
    }
}
