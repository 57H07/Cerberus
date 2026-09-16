using System.Diagnostics;
using Cerberus.Application.Services;
using Cerberus.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cerberus.Web.Controllers;

public class HomeController(IOrganizationManagementService organizationService) : Controller
{
    [Authorize]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await organizationService.GetAdministrationAccessAsync(cancellationToken));

    [AllowAnonymous, IgnoreAntiforgeryToken]
    public IActionResult Error() => View(new ErrorViewModel(Activity.Current?.Id ?? HttpContext.TraceIdentifier));

    [AllowAnonymous, IgnoreAntiforgeryToken, Route("Home/Status/{code:int}")]
    public IActionResult Status(int code)
    {
        ViewData["StatusCode"] = code;
        return View();
    }
}
