using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;
using PivotController.Reports.Services;

namespace PivotController.Reports.Controllers
{
    /// <summary>
    /// Report Sharing API (spec §6.2–§6.7): report CRUD under /api/reportsharing/reports,
    /// plus GET/PUT /api/config/reportsharing. Endpoints return 404 while disabled.
    /// </summary>
    [ApiController]
    [Route("api")]
    public class ReportSharingController : ControllerBase
    {
        private readonly ReportSharingService _reports;
        private readonly ReportSharingSettings _settings;
        private readonly ReportSharingSettingsService _settingsService;

        public ReportSharingController(ReportSharingService reports, ReportSharingSettings settings,
            ReportSharingSettingsService settingsService)
        {
            _reports = reports;
            _settings = settings;
            _settingsService = settingsService;
        }

        /// <summary>Save a report (spec §6.3.1). 404 when the endpoint is disabled (§6.2.1).</summary>
        [HttpPost("reportsharing/reports")]
        public IActionResult Save([FromBody] JsonElement body)
        {
            if (!_settings.EndpointEnabled)
            {
                return NotFound(new { error = "Report sharing disabled" });
            }
            if (body.ValueKind != JsonValueKind.Object)
            {
                return UnprocessableEntity(new { error = "Body must be a JSON object." });
            }
            string? name = body.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            string? owner = body.TryGetProperty("owner", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;
            object? payload = null;
            if (body.TryGetProperty("payload", out var p))
            {
                payload = Newtonsoft.Json.Linq.JToken.Parse(p.GetRawText());
            }

            var (report, errors) = _reports.Save(name ?? "", owner, payload);
            if (report == null)
            {
                // Quota rejection (clarified §6.5) surfaces as 507 Insufficient Storage.
                if (errors.Any(e => e.Contains("quota")))
                {
                    return StatusCode(StatusCodes.Status507InsufficientStorage, new { errors });
                }
                return UnprocessableEntity(new { errors });
            }
            return StatusCode(StatusCodes.Status201Created, new
            {
                reportId = report.ReportId,
                createdAt = report.CreatedAt,
                sizeBytes = report.SizeBytes
            });
        }

        /// <summary>Retrieve a report (spec §6.3.2); 410 Gone when expired (§6.4.3).</summary>
        [HttpGet("reportsharing/reports/{reportId}")]
        public IActionResult Get(string reportId)
        {
            if (!_settings.EndpointEnabled)
            {
                return NotFound(new { error = "Report sharing disabled" });
            }
            var (report, expired) = _reports.GetReport(reportId);
            if (report == null)
            {
                return expired
                    ? StatusCode(StatusCodes.Status410Gone, new { error = "Report expired" })
                    : NotFound(new { error = "Report not found" });
            }
            // Payload is stored as a Newtonsoft JToken; re-parse into System.Text.Json so the
            // API response serializes the payload verbatim (no JObject-internal structure).
            var payloadText = report.Payload?.ToString() ?? "null";
            return Ok(new
            {
                reportId = report.ReportId,
                name = report.Name,
                owner = report.Owner,
                createdAt = report.CreatedAt,
                expiresAt = report.ExpiresAt,
                sizeBytes = report.SizeBytes,
                payload = System.Text.Json.JsonDocument.Parse(payloadText).RootElement
            });
        }

        /// <summary>List reports (spec §6.3.3): metadata only, no payloads.</summary>
        [HttpGet("reportsharing/reports")]
        public IActionResult List()
        {
            if (!_settings.EndpointEnabled)
            {
                return NotFound(new { error = "Report sharing disabled" });
            }
            return Ok(new { reports = _reports.ListReports() });
        }

        /// <summary>Delete a report (spec §6.3.4).</summary>
        [HttpDelete("reportsharing/reports/{reportId}")]
        public IActionResult Delete(string reportId)
        {
            if (!_settings.EndpointEnabled)
            {
                return NotFound(new { error = "Report sharing disabled" });
            }
            return _reports.Delete(reportId) ? NoContent() : NotFound(new { error = "Report not found" });
        }

        /// <summary>Get report sharing settings (spec §6.7.1).</summary>
        [HttpGet("config/reportsharing")]
        public ActionResult<ReportSharingSettings> GetSettings() => Ok(_settingsService.Current);

        /// <summary>Update report sharing settings (spec §6.7.2); StoragePath requires restart.</summary>
        [HttpPut("config/reportsharing")]
        public IActionResult UpdateSettings([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return UnprocessableEntity(new { error = "Body must be a JSON object." });
            }
            bool? endpointEnabled = body.TryGetProperty("endpointEnabled", out var e)
                && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False) ? e.GetBoolean() : null;
            string? storagePath = body.TryGetProperty("storagePath", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : null;
            int? expiration = null;
            if (body.TryGetProperty("reportExpirationTimeMinutes", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt32(out var ev)) expiration = ev;
            int? maxMB = null;
            if (body.TryGetProperty("maxFolderSizeMB", out var mb) && mb.ValueKind == JsonValueKind.Number && mb.TryGetInt32(out var mv)) maxMB = mv;
            bool? autoDelete = body.TryGetProperty("autoDelete", out var ad)
                && (ad.ValueKind == JsonValueKind.True || ad.ValueKind == JsonValueKind.False) ? ad.GetBoolean() : null;

            var (settings, requiresRestart, errors) = _settingsService.ApplyPatch(
                endpointEnabled, storagePath, expiration, maxMB, autoDelete);
            if (errors.Count > 0)
            {
                return UnprocessableEntity(new { errors });
            }
            return Ok(new { settings, requiresRestart });
        }
    }
}