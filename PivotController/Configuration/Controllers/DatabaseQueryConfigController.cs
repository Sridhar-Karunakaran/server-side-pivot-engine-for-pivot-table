using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;

namespace PivotController.Configuration.Controllers
{
    /// <summary>Database Query settings API per spec §3.3: GET/PUT /api/config/databasequery.</summary>
    [ApiController]
    [Route("api/config")]
    public class DatabaseQueryConfigController : ControllerBase
    {
        private readonly DatabaseQuerySettingsService _databaseQuery;

        public DatabaseQueryConfigController(DatabaseQuerySettingsService databaseQuery)
        {
            _databaseQuery = databaseQuery;
        }

        /// <summary>Returns the current database query settings (spec §3.3.1).</summary>
        [HttpGet("databasequery")]
        public ActionResult<DatabaseQuerySettings> Get() => Ok(_databaseQuery.Current);

        /// <summary>
        /// Merge-patch of database query settings (spec §3.3.2). Both options apply
        /// immediately (spec §3.2.4); no restart is required.
        /// </summary>
        [HttpPut("databasequery")]
        public IActionResult Update([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return UnprocessableEntity(new { error = "Body must be a JSON object." });
            }
            bool? commandTimeoutEnabled = null;
            if (body.TryGetProperty("commandTimeoutEnabled", out var ct) && ct.ValueKind == JsonValueKind.True || ct.ValueKind == JsonValueKind.False)
            {
                commandTimeoutEnabled = ct.GetBoolean();
            }
            int? commandTimeLimit = null;
            if (body.TryGetProperty("commandTimeLimit", out var cl) && cl.ValueKind == JsonValueKind.Number)
            {
                if (!cl.TryGetInt32(out var v))
                {
                    return UnprocessableEntity(new { error = "commandTimeLimit must be an integer." });
                }
                commandTimeLimit = v;
            }
            var (settings, requiresRestart, errors) = _databaseQuery.ApplyPatch(commandTimeoutEnabled, commandTimeLimit);
            if (errors.Count > 0)
            {
                return UnprocessableEntity(new { errors });
            }
            return Ok(new { settings, requiresRestart });
        }
    }
}