using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cerberus.DemoClient.Controllers;

public class HomeController(IHttpClientFactory httpClientFactory, IConfiguration configuration) : Controller
{
    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return View("Anonymous");
        }

        var accessToken = await HttpContext.GetTokenAsync("access_token");
        ViewData["AccessTokenClaims"] = accessToken is null
            ? []
            : new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.Select(c => new KeyValuePair<string, string>(c.Type, c.Value)).ToList();
        ViewData["ExpiresAt"] = await HttpContext.GetTokenAsync("expires_at");
        ViewData["HasRefreshToken"] = await HttpContext.GetTokenAsync("refresh_token") is not null;
        return View();
    }

    [HttpGet]
    public IActionResult Login(string? organization)
        => Challenge(
            new AuthenticationProperties(new Dictionary<string, string?> { ["organization"] = organization }) { RedirectUri = "/" },
            OpenIdConnectDefaults.AuthenticationScheme);

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public IActionResult Logout()
        => SignOut(new AuthenticationProperties { RedirectUri = "/" },
            CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme);

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> CallApi(string organizationId)
    {
        var accessToken = await HttpContext.GetTokenAsync("access_token");
        var client = httpClientFactory.CreateClient("api");
        // The API is hosted by this application; inside a container the public port differs from the listening one.
        var apiBase = configuration["Api:BaseAddress"] ?? $"{Request.Scheme}://{Request.Host}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{apiBase.TrimEnd('/')}/api/organizations/{organizationId}/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request);
        TempData["ApiResult"] = $"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}";
        return RedirectToAction(nameof(Index));
    }

    public IActionResult Error() => View();
}
