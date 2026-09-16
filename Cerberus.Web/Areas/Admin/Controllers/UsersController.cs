using Cerberus.Application.Common;
using Cerberus.Application.DTOs;
using Cerberus.Application.Services;
using Cerberus.Web.Extensions;
using Cerberus.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Cerberus.Web.Areas.Admin.Controllers;

public class UsersController(IUserAdministrationService userService, IPlatformAdministrationService platformService) : AdminControllerBase
{
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        ViewData["Search"] = search;
        return View(await userService.ListAsync(new PagedFilter { Page = page, Search = search }, cancellationToken));
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
        => View(new UserDetailsViewModel(await userService.GetAsync(id, cancellationToken), await platformService.ListPlatformRolesAsync(cancellationToken)));

    [HttpGet]
    public IActionResult Create() => View("Form", new UserFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(UserFormViewModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(model.Password), "An initial password is required.");
        }

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var id = await userService.CreateAsync(
            new CreateUserDto(model.FirstName, model.LastName, model.Email, model.UserName, model.Password!, model.EmailConfirmed), cancellationToken);
        this.NotifySuccess("User created.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var user = (await userService.GetAsync(id, cancellationToken)).User;
        return View("Form", new UserFormViewModel
        {
            Id = id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            UserName = user.UserName,
            EmailConfirmed = user.EmailConfirmed
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, UserFormViewModel model, CancellationToken cancellationToken)
    {
        ModelState.Remove(nameof(model.Password));
        if (!ModelState.IsValid)
        {
            model.Id = id;
            return View("Form", model);
        }

        await userService.UpdateAsync(id, new UpdateUserDto(model.FirstName, model.LastName, model.Email, model.UserName), cancellationToken);
        this.NotifySuccess("User updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Disable(Guid id, CancellationToken cancellationToken)
    {
        await userService.DisableAsync(id, cancellationToken);
        this.NotifySuccess("User disabled: sessions and tokens were revoked.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Enable(Guid id, CancellationToken cancellationToken)
    {
        await userService.EnableAsync(id, cancellationToken);
        this.NotifySuccess("User enabled.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> AssignRole(Guid id, Guid roleId, CancellationToken cancellationToken)
    {
        await platformService.AssignPlatformRoleAsync(id, roleId, cancellationToken);
        this.NotifySuccess("Role assigned.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> UnassignRole(Guid id, Guid assignmentId, CancellationToken cancellationToken)
    {
        await platformService.UnassignPlatformRoleAsync(assignmentId, cancellationToken);
        this.NotifySuccess("Role removed.");
        return RedirectToAction(nameof(Details), new { id });
    }
}
