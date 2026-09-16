using Cerberus.Application.Common;
using Cerberus.Application.DTOs;
using Cerberus.Application.Services;
using Cerberus.Web.Extensions;
using Cerberus.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Cerberus.Web.Areas.Admin.Controllers;

public class OrganizationsController(IPlatformAdministrationService platformService) : AdminControllerBase
{
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        ViewData["Search"] = search;
        return View(await platformService.ListOrganizationsAsync(new PagedFilter { Page = page, Search = search }, cancellationToken));
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
        => View(new OrganizationDetailsViewModel(
            await platformService.GetOrganizationAsync(id, cancellationToken),
            await platformService.ListOrganizationAdministratorsAsync(id, cancellationToken)));

    [HttpGet]
    public IActionResult Create() => View(new OrganizationFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(OrganizationFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var id = await platformService.CreateOrganizationAsync(new CreateOrganizationDto(model.Name, model.Slug), cancellationToken);
        this.NotifySuccess("Organization created.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Rename(Guid id, string name, CancellationToken cancellationToken)
    {
        await platformService.RenameOrganizationAsync(id, name, cancellationToken);
        this.NotifySuccess("Organization renamed.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Suspend(Guid id, CancellationToken cancellationToken)
    {
        await platformService.SuspendOrganizationAsync(id, cancellationToken);
        this.NotifySuccess("Organization suspended: its tokens were revoked.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        await platformService.ActivateOrganizationAsync(id, cancellationToken);
        this.NotifySuccess("Organization activated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> AddAdministrator(Guid id, string login, CancellationToken cancellationToken)
    {
        await platformService.AddOrganizationAdministratorAsync(id, login, cancellationToken);
        this.NotifySuccess("Administrator added.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveAdministrator(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        await platformService.RemoveOrganizationAdministratorAsync(id, userId, cancellationToken);
        this.NotifySuccess("Administrator removed.");
        return RedirectToAction(nameof(Details), new { id });
    }
}
