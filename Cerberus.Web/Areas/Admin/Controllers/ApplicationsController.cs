using Cerberus.Application.Common;
using Cerberus.Application.DTOs;
using Cerberus.Application.Services;
using Cerberus.Web.Extensions;
using Cerberus.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Cerberus.Web.Areas.Admin.Controllers;

public class ApplicationsController(IClientAdministrationService clientService, IPlatformAdministrationService platformService) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await clientService.ListApplicationsAsync(cancellationToken));

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
        => View(new ApplicationDetailsViewModel(
            await clientService.GetApplicationAsync(id, cancellationToken),
            await clientService.ListClientsAsync(id, cancellationToken)));

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
        => View(new ApplicationFormViewModel { Organizations = await ListOrganizationsAsync(cancellationToken) });

    [HttpPost]
    public async Task<IActionResult> Create(ApplicationFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Organizations = await ListOrganizationsAsync(cancellationToken);
            return View(model);
        }

        var id = await clientService.CreateApplicationAsync(new SaveApplicationDto(model.Name, model.Description, model.OwnerOrganizationId), cancellationToken);
        this.NotifySuccess("Application created.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> SetActive(Guid id, bool active, CancellationToken cancellationToken)
    {
        await clientService.SetApplicationActiveAsync(id, active, cancellationToken);
        this.NotifySuccess(active ? "Application enabled." : "Application disabled: its tokens were revoked.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> GrantOrganization(Guid id, string slug, CancellationToken cancellationToken)
    {
        await clientService.GrantOrganizationAsync(id, slug, cancellationToken);
        this.NotifySuccess("Organization granted.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveOrganization(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        await clientService.RemoveOrganizationAsync(id, organizationId, cancellationToken);
        this.NotifySuccess("Organization access removed.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> CreateClient(Guid id, CancellationToken cancellationToken)
        => View("ClientForm", new ClientFormViewModel { ApplicationId = id, AvailableScopes = await clientService.ListAssignableScopeNamesAsync(cancellationToken) });

    [HttpPost]
    public async Task<IActionResult> CreateClient(ClientFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.AvailableScopes = await clientService.ListAssignableScopeNamesAsync(cancellationToken);
            return View("ClientForm", model);
        }

        var secret = await clientService.CreateClientAsync(new CreateOidcClientDto(
            model.ApplicationId, model.ClientId, model.DisplayName, model.ClientType, model.ConsentPolicy, model.ConsentLifetimeDays,
            model.RedirectUris.SplitLines(), model.PostLogoutRedirectUris.SplitLines(), model.AllowedScopes), cancellationToken);

        // Rendered directly (never stored in TempData or logs): the secret is displayed exactly once.
        ViewData["ApplicationId"] = model.ApplicationId;
        return View("ClientSecret", secret);
    }

    [HttpGet]
    public async Task<IActionResult> EditClient(Guid id, CancellationToken cancellationToken)
    {
        var client = await clientService.GetClientAsync(id, cancellationToken);
        return View("ClientForm", new ClientFormViewModel
        {
            Id = client.Id,
            ApplicationId = client.ApplicationId,
            ClientId = client.ClientId,
            DisplayName = client.DisplayName,
            ClientType = client.ClientType,
            ConsentPolicy = client.ConsentPolicy,
            ConsentLifetimeDays = client.ConsentLifetime is null ? null : (int)client.ConsentLifetime.Value.TotalDays,
            RedirectUris = string.Join(Environment.NewLine, client.RedirectUris),
            PostLogoutRedirectUris = string.Join(Environment.NewLine, client.PostLogoutRedirectUris),
            AllowedScopes = client.AllowedScopes.ToList(),
            AvailableScopes = await clientService.ListAssignableScopeNamesAsync(cancellationToken)
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditClient(Guid id, ClientFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Id = id;
            model.AvailableScopes = await clientService.ListAssignableScopeNamesAsync(cancellationToken);
            return View("ClientForm", model);
        }

        await clientService.UpdateClientAsync(id, new UpdateOidcClientDto(
            model.DisplayName, model.ConsentPolicy, model.ConsentLifetimeDays,
            model.RedirectUris.SplitLines(), model.PostLogoutRedirectUris.SplitLines(), model.AllowedScopes), cancellationToken);
        this.NotifySuccess("Client updated.");
        return RedirectToAction(nameof(Details), new { id = model.ApplicationId });
    }

    [HttpPost]
    public async Task<IActionResult> RotateSecret(Guid id, CancellationToken cancellationToken)
    {
        var client = await clientService.GetClientAsync(id, cancellationToken);
        var secret = await clientService.RotateSecretAsync(id, cancellationToken);
        ViewData["ApplicationId"] = client.ApplicationId;
        return View("ClientSecret", secret);
    }

    [HttpPost]
    public async Task<IActionResult> SetClientActive(Guid id, bool active, CancellationToken cancellationToken)
    {
        var client = await clientService.GetClientAsync(id, cancellationToken);
        await clientService.SetClientActiveAsync(id, active, cancellationToken);
        this.NotifySuccess(active ? "Client enabled." : "Client disabled: its tokens were revoked.");
        return RedirectToAction(nameof(Details), new { id = client.ApplicationId });
    }

    private async Task<IReadOnlyList<OrganizationDto>> ListOrganizationsAsync(CancellationToken cancellationToken)
        => (await platformService.ListOrganizationsAsync(new PagedFilter { PageSize = PagedFilter.MaxPageSize }, cancellationToken)).Items;
}
