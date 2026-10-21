using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PivotController.Reports.Services;

namespace PivotController.Reports.Controllers
{
    /// <summary>
    /// Report Sharing settings API (spec §6.7): GET/PUT /api/config/reportsharing.
    /// All fields except pathToFolder apply immediately (pathToFolder requires restart).
    /// </summary>
    [ApiController]
    [Route("api/config")]
    public class ReportSharingConfigController : ControllerBase
    {
        private readonly ReportSharingService _reportSharing;

        public ReportSharingConfigController(ReportSharingService reportSharing)
        {
            _reportSharing = reportSharing;
        }

        /// <summary>Get Report Sharing settings (spec §6.7.1).</summary>
        [HttpGet("reportsharing")]
        public IActionResult Get() => Ok(new
        {
            endpointEnabled = _reportSharing.CurrentSettings.EndpointEnabled,
            pathToFolder = _reportSharing.CurrentSettings.PathToFolder,
            reportExpirationTime = _reportSharing.CurrentSettings.ReportExpirationTime,
            maxSizeOfFolder = _reportSharing.CurrentSettings.MaxSizeOfFolder,
            autoDeletingEnabled = _reportSharing.CurrentSettings.AutoDeletingEnabled,
            reportsFolder = _reportSharing.ReportsFolderPath,
            folderSizeBytes = _reportSharing.FolderSizeBytes(),
            requiresRestartFields = new[] { "pathToFolder" }
        });

        /// <summary>Update Report Sharing settings (spec §6.7.2): merge-patch.</summary>
        [HttpPut("reportsharing")]
        public IActionResult Update([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return UnprocessableEntity(new { error = "Body must be a JSON object." });
            }

            bool? endpointEnabled = null;
            if (body.TryGetProperty("endpointEnabled", out var e))
            {
                if (e.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return UnprocessableEntity(new { error = "endpointEnabled must be a boolean." });
                }
                endpointEnabled = e.GetBoolean();
            }

            string? pathToFolder = null;
            if (body.TryGetProperty("pathToFolder", out var p))
            {
                if (p.ValueKind != JsonValueKind.String)
                {
                    return UnprocessableEntity(new { error = "pathToFolder must be a string." });
                }
                pathToFolder = p.GetString();
            }

            int? reportExpirationTime = null;
            if (body.TryGetProperty("reportExpirationTime", out var ex))
            {
                if (ex.ValueKind != JsonValueKind.Number || !ex.TryGetInt32(out var expiration))
                {
                    return UnprocessableEntity(new { error = "reportExpirationTime must be an integer (minutes; 0 = never expire)." });
                }
                reportExpirationTime = expiration;
            }

            int? maxSizeOfFolder = null;
            if (body.TryGetProperty("maxSizeOfFolder", out var m))
            {
                if (m.ValueKind != JsonValueKind.Number || !m.TryGetInt32(out var size))
                {
                    return UnprocessableEntity(new { error = "maxSizeOfFolder must be an integer (MB; 0 = unlimited)." });
                }
                maxSizeOfFolder = size;
            }

            bool? autoDeletingEnabled = null;
            if (body.TryGetProperty("autoDeletingEnabled", out var a))
            {
                if (a.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return UnprocessableEntity(new { error = "autoDeletingEnabled must be a boolean." });
                }
                autoDeletingEnabled = a.GetBoolean();
            }

            var (settings, requiresRestart, errors) = _reportSharing.ApplyPatch(
                endpointEnabled, pathToFolder, reportExpirationTime, maxSizeOfFolder, autoDeletingEnabled);
            if (errors.Count > 0)
            {
                return UnprocessableEntity(new { errors });
            }
            return Ok(new { settings, requiresRestart, restartFields = requiresRestart ? new[] { "pathToFolder" } : Array.Empty<string>() });
        }
    }
}