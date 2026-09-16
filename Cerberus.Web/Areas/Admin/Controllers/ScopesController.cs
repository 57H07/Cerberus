using Cerberus.Application.DTOs;
using Cerberus.Application.Services;
using Cerberus.Web.Extensions;
using Cerberus.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Cerberus.Web.Areas.Admin.Controllers;

public class ScopesController(IClientAdministrationService clientService) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await clientService.ListScopesAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(ScopeFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            this.NotifyError(string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            return RedirectToAction(nameof(Index));
        }

        await clientService.CreateScopeAsync(new SaveApiScopeDto(model.Name, model.DisplayName, model.Description, model.Resource), cancellationToken);
        this.NotifySuccess("Scope created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, ScopeFormViewModel model, CancellationToken cancellationToken)
    {
        await clientService.UpdateScopeAsync(id, new SaveApiScopeDto(model.Name, model.DisplayName, model.Description, model.Resource), cancellationToken);
        this.NotifySuccess("Scope updated.");
        return RedirectToAction(nameof(Index));
    }
}
