using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PivotController.Reports.Services;

namespace PivotController.Reports.Controllers
{
    /// <summary>
    /// Report Sharing API (spec §6.3): POST / GET / DELETE under /api/reportsharing/reports.
    /// When the endpoint is disabled, all report operations return 404 "Report sharing
    /// disabled" (spec §6.2.1).
    /// </summary>
    [ApiController]
    [Route("api/reportsharing")]
    public class ReportsController : ControllerBase
    {
        private readonly ReportSharingService _reports;

        public ReportsController(ReportSharingService reports)
        {
            _reports = reports;
        }

        /// <summary>Save a new report (spec §6.3.1): 201 + metadata; 404 when disabled; 507 on quota.</summary>
        [HttpPost("reports")]
        public IActionResult Save([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return UnprocessableEntity(new { error = "Body must be a JSON object." });
            }

            string name = "", owner = "";
            if (!body.TryGetProperty("payload", out var payload)
                || payload.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                return UnprocessableEntity(new { error = "'payload' is required and must be a JSON object or array (opaque report blob)." });
            }
            string payloadJson;
            try
            {
                payloadJson = payload.GetRawText();
            }
            catch (Exception)
            {
                return UnprocessableEntity(new { error = "'payload' must be valid JSON." });
            }

            if (body.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            {
                name = n.GetString() ?? "";
            }
            if (body.TryGetProperty("owner", out var o) && o.ValueKind == JsonValueKind.String)
            {
                owner = o.GetString() ?? "";
            }

            var (result, meta) = _reports.Save(name, owner, payloadJson);
            return result switch
            {
                ReportSharingService.SaveResult.EndpointDisabled =>
                    NotFound(new { error = "Report sharing disabled" }),
                ReportSharingService.SaveResult.InvalidPayload =>
                    UnprocessableEntity(new { error = "'payload' must be valid JSON." }),
                ReportSharingService.SaveResult.QuotaExceeded =>
                    Problem(
                        title: "Insufficient Storage",
                        detail: $"Report folder quota ({_reports.CurrentSettings.MaxSizeOfFolder} MB) exceeded.",
                        statusCode: StatusCodes.Status507InsufficientStorage),
                _ => StatusCode(StatusCodes.Status201Created, meta)
            };
        }

        /// <summary>Retrieve a report payload (spec §6.3.2); 410 Gone when expired (spec §6.4.3).</summary>
        [HttpGet("reports/{reportId}")]
        public IActionResult Get(string reportId)
        {
            var (result, meta, payload) = _reports.Get(reportId);
            return result switch
            {
                ReportSharingService.GetResult.EndpointDisabled =>
                    NotFound(new { error = "Report sharing disabled" }),
                ReportSharingService.GetResult.NotFound =>
                    NotFound(new { error = $"Report '{reportId}' not found." }),
                ReportSharingService.GetResult.Expired =>
                    StatusCode(StatusCodes.Status410Gone, new { error = $"Report '{reportId}' has expired.", reportId, meta?.ExpiresAt }),
                _ => Ok(new { meta!.ReportId, meta.Name, meta.Owner, meta.CreatedAt, meta.ExpiresAt, meta.SizeBytes, payload })
            };
        }

        /// <summary>List report metadata (spec §6.3.3) — payloads not included.</summary>
        [HttpGet("reports")]
        public IActionResult List()
        {
            if (!_reports.EndpointEnabled)
            {
                return NotFound(new { error = "Report sharing disabled" });
            }
            return Ok(new { reports = _reports.List().OrderBy(m => m.CreatedAt).ToArray() });
        }

        /// <summary>Delete a report (spec §6.3.4): 204.</summary>
        [HttpDelete("reports/{reportId}")]
        public IActionResult Delete(string reportId)
        {
            var result = _reports.Delete(reportId);
            return result switch
            {
                ReportSharingService.DeleteResult.EndpointDisabled =>
                    NotFound(new { error = "Report sharing disabled" }),
                ReportSharingService.DeleteResult.NotFound =>
                    NotFound(new { error = $"Report '{reportId}' not found." }),
                _ => NoContent()
            };
        }
    }
}