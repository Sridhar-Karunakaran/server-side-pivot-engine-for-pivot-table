using Microsoft.AspNetCore.Mvc;

namespace PivotController.Controllers;

/// <summary>
/// Liveness endpoint (custom-data-server spec §7.6): GET /health returns 200
/// when the Data Server is responsive. Used by the Admin Panel's reachability
/// probe (server-lifecycle spec §4) — intentionally unauthenticated and free
/// of any configuration detail.
/// </summary>
[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health() => Ok(new { status = "healthy" });
}
