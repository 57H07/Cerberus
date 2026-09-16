using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cerberus.DemoClient.Controllers;

/// <summary>
/// Example resource server. The JWT only proves who the caller is and which organization Cerberus validated;
/// the application still performs its own authorization on its resources (here: the organization in the URL must
/// match the token, and writes require a business role mapped locally).
/// </summary>
[ApiController]
[Route("api/organizations/{organizationId:guid}/orders")]
[Authorize(Policy = DemoPolicies.Api)]
public class OrdersApiController : ControllerBase
{
    private static readonly string[] WriterRoles = ["Organization administrator", "Sales"];

    [HttpGet]
    public IActionResult List(Guid organizationId)
    {
        if (!IsSameOrganization(organizationId))
        {
            return Forbid();
        }

        return Ok(new
        {
            organizationId,
            caller = User.FindFirst("sub")?.Value,
            roles = User.FindAll("org_roles").Select(c => c.Value),
            orders = new[] { new { id = 1, label = $"Order for {organizationId:N}" } }
        });
    }

    [HttpPost]
    public IActionResult Create(Guid organizationId)
    {
        if (!IsSameOrganization(organizationId) || !User.FindAll("org_roles").Any(r => WriterRoles.Contains(r.Value)))
        {
            return Forbid();
        }

        return Created($"api/organizations/{organizationId}/orders/2", new { id = 2 });
    }

    private bool IsSameOrganization(Guid organizationId)
        => Guid.TryParse(User.FindFirst("org_id")?.Value, out var tokenOrganization) && tokenOrganization == organizationId;
}
