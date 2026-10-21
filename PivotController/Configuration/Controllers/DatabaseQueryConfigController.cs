using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;

namespace PivotController.Configuration.Controllers
{
    /// <summary>Database Query settings API (spec §3.3): GET/PUT /api/config/databasequery (merge-patch).</summary>
    [ApiController]
    [Route("api/config")]
    public class DatabaseQueryConfigController : ControllerBase
    {
        private readonly DatabaseQuerySettingsService _dbQuery;

        public DatabaseQueryConfigController(DatabaseQuerySettingsService dbQuery)
        {
            _dbQuery = dbQuery;
        }

        /// <summary>Get database query settings (spec §3.3.1).</summary>
        [HttpGet("databasequery")]
        public ActionResult<DatabaseQuerySettings> Get() => Ok(_dbQuery.Current);

        /// <summary>Update database query settings (spec §3.3.2); applied immediately.</summary>
        [HttpPut("databasequery")]
        public IActionResult Update([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return UnprocessableEntity(new { error = "Body must be a JSON object." });
            }
            bool? enabled = body.TryGetProperty("commandTimeoutEnabled", out var e)
                && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False) ? e.GetBoolean() : null;
            int? limit = null;
            if (body.TryGetProperty("commandTimeLimit", out var l) && l.ValueKind == JsonValueKind.Number)
            {
                if (!l.TryGetInt32(out var lv))
                {
                    return UnprocessableEntity(new { error = "CommandTimeLimit must be an integer." });
                }
                limit = lv;
            }
            var (settings, errors) = _dbQuery.ApplyPatch(enabled, limit);
            if (errors.Count > 0)
            {
                return UnprocessableEntity(new { errors });
            }
            return Ok(new { settings, requiresRestart = false });
        }
    }
}