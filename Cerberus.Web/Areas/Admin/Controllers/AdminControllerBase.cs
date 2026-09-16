using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cerberus.Web.Areas.Admin.Controllers;

/// <summary>
/// Platform administration. Controllers only require an interactive session: every permission check is performed
/// by the application services, so the rules stay testable without HTTP.
/// </summary>
[Area("Admin")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public abstract class AdminControllerBase : Controller;
