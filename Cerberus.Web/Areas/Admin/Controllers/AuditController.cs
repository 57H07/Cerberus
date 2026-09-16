using Cerberus.Application.Common;
using Cerberus.Application.Services;
using Cerberus.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Cerberus.Web.Areas.Admin.Controllers;

public class AuditController(IPlatformAdministrationService platformService) : AdminControllerBase
{
    public async Task<IActionResult> Index(string? prefix, int page = 1, CancellationToken cancellationToken = default)
    {
        var entries = await platformService.GetAuditAsync(new AuditFilter { Page = page, PageSize = 50, ActionPrefix = prefix }, cancellationToken);
        return View(new AuditViewModel(entries, prefix));
    }
}
