using Cerberus.Application.DTOs;
using Cerberus.Application.Services;
using Cerberus.Domain.Authorization;
using Cerberus.Web.Extensions;
using Cerberus.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Cerberus.Web.Areas.Admin.Controllers;

public class RolesController(IPlatformAdministrationService platformService) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await platformService.ListPlatformRolesAsync(cancellationToken));

    [HttpGet]
    public IActionResult Create() => View("Form", NewForm());

    [HttpPost]
    public async Task<IActionResult> Create(RoleFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Form", WithPermissions(model));
        }

        await platformService.CreatePlatformRoleAsync(new SaveRoleDto(model.Name, model.Description, model.Permissions), cancellationToken);
        this.NotifySuccess("Role created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var role = (await platformService.ListPlatformRolesAsync(cancellationToken)).FirstOrDefault(r => r.Id == id);
        if (role is null)
        {
            return NotFound();
        }

        return View("Form", WithPermissions(new RoleFormViewModel
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            Permissions = role.Permissions.ToList()
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, RoleFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Id = id;
            return View("Form", WithPermissions(model));
        }

        await platformService.UpdatePlatformRoleAsync(id, new SaveRoleDto(model.Name, model.Description, model.Permissions), cancellationToken);
        this.NotifySuccess("Role updated.");
        return RedirectToAction(nameof(Index));
    }

    private static RoleFormViewModel NewForm() => WithPermissions(new RoleFormViewModel());

    private static RoleFormViewModel WithPermissions(RoleFormViewModel model)
    {
        model.AvailablePermissions = PermissionCatalog.All.Where(p => p.Scope == RoleScope.Platform).ToList();
        return model;
    }
}
