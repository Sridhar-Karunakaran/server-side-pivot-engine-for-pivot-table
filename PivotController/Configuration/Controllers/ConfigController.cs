using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;

namespace PivotController.Configuration.Controllers
{
    /// <summary>General settings API per spec §1.5: GET/PUT /api/config/general (merge-patch).</summary>
    [ApiController]
    [Route("api/config")]
    public class ConfigController : ControllerBase
    {
        private readonly GeneralSettingsService _general;

        public ConfigController(GeneralSettingsService general)
        {
            _general = general;
        }

        /// <summary>Returns the current general settings.</summary>
        [HttpGet("general")]
        public ActionResult<GeneralSettings> GetGeneral() => Ok(_general.Current);

        /// <summary>
        /// Local testing helper (spec §1.3): emits one log message at each level via the
        /// minimum-level-filtered provider. Messages below the configured MinimumLogLevel
        /// are suppressed, so calling this before/after a level change shows the filter
        /// working in the local console.
        /// </summary>
        [HttpPost("general/test-log")]
        public IActionResult TestLog([FromServices] ILogger<ConfigController> logger)
        {
            logger.LogInformation("TEST-LOG: this is an Information message");
            logger.LogWarning("TEST-LOG: this is a Warning message");
            logger.LogError("TEST-LOG: this is an Error message");
            logger.LogCritical("TEST-LOG: this is a Critical (Fatal) message");
            return Ok(new { message = "Emitted Information/Warning/Error/Critical test log entries. Check the console/log output — entries below the current MinimumLogLevel are suppressed." });
        }

        /// <summary>
        /// Merge-patch of general settings. Only supplied properties change. Response reports
        /// requiresRestart: Port/DataFolder changes take effect after restart; MinimumLogLevel
        /// applies immediately.
        /// </summary>
        [HttpPut("general")]
        public IActionResult UpdateGeneral([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return UnprocessableEntity(new { error = "Body must be a JSON object." });
            }
            int? port = null;
            if (body.TryGetProperty("port", out var portElement)
                && portElement.ValueKind == JsonValueKind.Number)
            {
                if (portElement.TryGetInt32(out var p))
                {
                    port = p;
                }
                else
                {
                    return UnprocessableEntity(new { error = "Port must be an integer." });
                }
            }
            string? logLevel = body.TryGetProperty("minimumLogLevel", out var levelElement)
                && levelElement.ValueKind == JsonValueKind.String
                ? levelElement.GetString()
                : null;
            string? dataFolder = body.TryGetProperty("dataFolder", out var folderElement)
                && folderElement.ValueKind == JsonValueKind.String
                ? folderElement.GetString()
                : null;

            var errors = _general.ValidatePatch(port, logLevel, dataFolder);
            if (errors.Count > 0)
            {
                return UnprocessableEntity(new { errors });
            }

            var (settings, requiresRestart) = _general.ApplyPatch(port, logLevel, dataFolder);
            return Ok(new
            {
                settings,
                requiresRestart
            });
        }
    }
}