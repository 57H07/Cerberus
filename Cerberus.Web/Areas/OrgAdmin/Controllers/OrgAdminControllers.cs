using Cerberus.Application.Authorization;
using Cerberus.Application.DTOs;
using Cerberus.Application.Common;
using Cerberus.Application.Services;
using Cerberus.Domain.Authorization;
using Cerberus.Web.Extensions;
using Cerberus.Web.ViewModels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cerberus.Web.Areas.OrgAdmin.Controllers;

/// <summary>
/// Organization administration. The organization id always comes from the route and is passed to the application
/// services, which verify the actor's active membership and permission in that organization before anything else.
/// </summary>
[Area("OrgAdmin")]
[Route("OrgAdmin/{organizationId:guid}/[controller]/[action]/{id:guid?}")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public abstract class OrgAdminControllerBase(IOrganizationManagementService organizationService, IAccessGuard accessGuard) : Controller
{
    protected IOrganizationManagementService OrganizationService { get; } = organizationService;

    protected Task<IReadOnlySet<string>> PermissionsAsync(Guid organizationId, CancellationToken cancellationToken)
        => accessGuard.GetOrganizationPermissionsAsync(organizationId, cancellationToken);
}

public class OrganizationController(IOrganizationManagementService organizationService, IAccessGuard accessGuard) : OrgAdminControllerBase(organizationService, accessGuard)
{
    [HttpGet("~/OrgAdmin/{organizationId:guid}")]
    public async Task<IActionResult> Index(Guid organizationId, CancellationToken cancellationToken)
        => View(new OrganizationHomeViewModel(
            await OrganizationService.GetOrganizationAsync(organizationId, cancellationToken),
            await PermissionsAsync(organizationId, cancellationToken)));

    [HttpPost]
    public async Task<IActionResult> Rename(Guid organizationId, string name, CancellationToken cancellationToken)
    {
        await OrganizationService.RenameAsync(organizationId, name, cancellationToken);
        this.NotifySuccess("Organization renamed.");
        return RedirectToAction(nameof(Index), new { organizationId });
    }

    [HttpGet]
    public async Task<IActionResult> Audit(Guid organizationId, int page = 1, CancellationToken cancellationToken = default)
    {
        ViewData["Organization"] = await OrganizationService.GetOrganizationAsync(organizationId, cancellationToken);
        var entries = await OrganizationService.GetAuditAsync(organizationId, new AuditFilter { Page = page, PageSize = 50 }, cancellationToken);
        return View(new AuditViewModel(entries, null));
    }
}

public class MembersController(IOrganizationManagementService organizationService, IAccessGuard accessGuard) : OrgAdminControllerBase(organizationService, accessGuard)
{
    [HttpGet("~/OrgAdmin/{organizationId:guid}/Members")]
    public async Task<IActionResult> Index(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await OrganizationService.GetOrganizationAsync(organizationId, cancellationToken);
        var members = await OrganizationService.ListMembersAsync(organizationId, cancellationToken);
        var roles = await OrganizationService.ListRolesAsync(organizationId, cancellationToken);
        IReadOnlyList<InvitationDto>? invitations = null;
        var permissions = await PermissionsAsync(organizationId, cancellationToken);
        if (permissions.Contains(PermissionCatalog.OrganizationInvitationsManage))
        {
            invitations = await OrganizationService.ListInvitationsAsync(organizationId, cancellationToken);
        }

        return View(new MembersViewModel(organization, members, roles, invitations));
    }

    [HttpPost]
    public async Task<IActionResult> Suspend(Guid organizationId, Guid id, CancellationToken cancellationToken)
        => await RunAsync(organizationId, () => OrganizationService.SuspendMemberAsync(organizationId, id, cancellationToken), "Member suspended.");

    [HttpPost]
    public async Task<IActionResult> Resume(Guid organizationId, Guid id, CancellationToken cancellationToken)
        => await RunAsync(organizationId, () => OrganizationService.ResumeMemberAsync(organizationId, id, cancellationToken), "Member resumed.");

    [HttpPost]
    public async Task<IActionResult> Remove(Guid organizationId, Guid id, CancellationToken cancellationToken)
        => await RunAsync(organizationId, () => OrganizationService.RemoveMemberAsync(organizationId, id, cancellationToken), "Member removed.");

    [HttpPost]
    public async Task<IActionResult> AssignRole(Guid organizationId, Guid id, Guid roleId, CancellationToken cancellationToken)
        => await RunAsync(organizationId, () => OrganizationService.AssignRoleAsync(organizationId, id, roleId, cancellationToken), "Role assigned.");

    [HttpPost]
    public async Task<IActionResult> UnassignRole(Guid organizationId, Guid id, CancellationToken cancellationToken)
        => await RunAsync(organizationId, () => OrganizationService.UnassignRoleAsync(organizationId, id, cancellationToken), "Role removed.");

    [HttpPost]
    public async Task<IActionResult> Invite(Guid organizationId, string email, CancellationToken cancellationToken)
        => await RunAsync(organizationId, () => OrganizationService.InviteAsync(organizationId, email, cancellationToken), "Invitation sent.");

    [HttpPost]
    public async Task<IActionResult> RevokeInvitation(Guid organizationId, Guid id, CancellationToken cancellationToken)
        => await RunAsync(organizationId, () => OrganizationService.RevokeInvitationAsync(organizationId, id, cancellationToken), "Invitation revoked.");

    private async Task<IActionResult> RunAsync(Guid organizationId, Func<Task> action, string message)
    {
        await action();
        this.NotifySuccess(message);
        return RedirectToAction(nameof(Index), new { organizationId });
    }
}

public class RolesController(IOrganizationManagementService organizationService, IAccessGuard accessGuard) : OrgAdminControllerBase(organizationService, accessGuard)
{
    [HttpGet("~/OrgAdmin/{organizationId:guid}/Roles")]
    public async Task<IActionResult> Index(Guid organizationId, CancellationToken cancellationToken)
        => View(new OrganizationRolesViewModel(
            await OrganizationService.GetOrganizationAsync(organizationId, cancellationToken),
            await OrganizationService.ListRolesAsync(organizationId, cancellationToken)));

    [HttpGet]
    public IActionResult Create(Guid organizationId) => View("Form", WithPermissions(new RoleFormViewModel()));

    [HttpPost]
    public async Task<IActionResult> Create(Guid organizationId, RoleFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Form", WithPermissions(model));
        }

        await OrganizationService.CreateRoleAsync(organizationId, new SaveRoleDto(model.Name, model.Description, model.Permissions), cancellationToken);
        this.NotifySuccess("Role created.");
        return RedirectToAction(nameof(Index), new { organizationId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        var role = await OrganizationService.GetRoleAsync(organizationId, id, cancellationToken);
        return View("Form", WithPermissions(new RoleFormViewModel
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            Permissions = role.Permissions.ToList()
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid organizationId, Guid id, RoleFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Id = id;
            return View("Form", WithPermissions(model));
        }

        await OrganizationService.UpdateRoleAsync(organizationId, id, new SaveRoleDto(model.Name, model.Description, model.Permissions), cancellationToken);
        this.NotifySuccess("Role updated.");
        return RedirectToAction(nameof(Index), new { organizationId });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        await OrganizationService.DeleteRoleAsync(organizationId, id, cancellationToken);
        this.NotifySuccess("Role deleted.");
        return RedirectToAction(nameof(Index), new { organizationId });
    }

    private static RoleFormViewModel WithPermissions(RoleFormViewModel model)
    {
        model.AvailablePermissions = PermissionCatalog.All.Where(p => p.Scope == RoleScope.Organization).ToList();
        return model;
    }
}

public class ApplicationsController(IOrganizationManagementService organizationService, IAccessGuard accessGuard) : OrgAdminControllerBase(organizationService, accessGuard)
{
    [HttpGet("~/OrgAdmin/{organizationId:guid}/Applications")]
    public async Task<IActionResult> Index(Guid organizationId, CancellationToken cancellationToken)
        => View(new OrganizationApplicationsViewModel(
            await OrganizationService.GetOrganizationAsync(organizationId, cancellationToken),
            await OrganizationService.ListApplicationsAsync(organizationId, cancellationToken)));

    [HttpPost]
    public async Task<IActionResult> SetEnabled(Guid organizationId, Guid id, bool enabled, CancellationToken cancellationToken)
    {
        await OrganizationService.SetApplicationEnabledAsync(organizationId, id, enabled, cancellationToken);
        this.NotifySuccess(enabled ? "Application enabled for the organization." : "Application disabled for the organization.");
        return RedirectToAction(nameof(Index), new { organizationId });
    }
}
