using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;

namespace PivotController.Configuration.Controllers
{
    /// <summary>
    /// Security settings API (spec §5.4): GET/PUT /api/config/security (merge-patch).
    /// The response never includes the password. Credentials (username/password) come
    /// from the Data Server configuration and are not settable via this API (Admin
    /// Panel later).
    /// </summary>
    [ApiController]
    [Route("api/config")]
    public class SecurityConfigController : ControllerBase
    {
        private readonly SecuritySettingsService _security;

        public SecurityConfigController(SecuritySettingsService security)
        {
            _security = security;
        }

        /// <summary>Get security settings (spec §5.4.1); password never returned.</summary>
        [HttpGet("security")]
        public ActionResult<SecuritySettings> Get() => Ok(_security.Current);

        /// <summary>Update security settings (spec §5.4.2); applied immediately.</summary>
        [HttpPut("security")]
        public IActionResult Update([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return UnprocessableEntity(new { error = "Body must be a JSON object." });
            }

            bool? enabled = null;
            if (body.TryGetProperty("authorization", out var authorization)
                && authorization.ValueKind == JsonValueKind.Object
                && authorization.TryGetProperty("enabled", out var e))
            {
                if (e.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return UnprocessableEntity(new { error = "authorization.enabled must be a boolean." });
                }
                enabled = e.GetBoolean();
            }

            string? allowOrigin = null;
            if (body.TryGetProperty("cors", out var cors)
                && cors.ValueKind == JsonValueKind.Object
                && cors.TryGetProperty("allowOrigin", out var ao)
                && ao.ValueKind == JsonValueKind.String)
            {
                allowOrigin = ao.GetString();
            }

            var (settings, errors) = _security.ApplyPatch(enabled, allowOrigin);
            if (errors.Count > 0)
            {
                return UnprocessableEntity(new { errors });
            }
            return Ok(new { settings });
        }
    }
}