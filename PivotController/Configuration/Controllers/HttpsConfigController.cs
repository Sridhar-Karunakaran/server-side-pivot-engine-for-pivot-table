using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;

namespace PivotController.Configuration.Controllers
{
    /// <summary>HTTPS settings API per spec §4.5: GET/PUT /api/config/https.</summary>
    [ApiController]
    [Route("api/config")]
    public class HttpsConfigController : ControllerBase
    {
        private readonly HttpsSettingsService _https;
        private readonly IWebHostEnvironment _environment;

        public HttpsConfigController(HttpsSettingsService https, IWebHostEnvironment environment)
        {
            _https = https;
            _environment = environment;
        }

        /// <summary>Returns current HTTPS settings (PFX password never included, spec §4.5.1).</summary>
        [HttpGet("https")]
        public ActionResult<HttpsSettings> Get() => Ok(_https.Current);

        /// <summary>
        /// Merge-patch of HTTPS settings (spec §4.5.2). All changes bind the Kestrel listener
        /// at startup, so the response always carries requiresRestart: true.
        /// </summary>
        [HttpPut("https")]
        public IActionResult Update([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return UnprocessableEntity(new { error = "Body must be a JSON object." });
            }
            bool? enabled = null;
            if (body.TryGetProperty("enabled", out var e) && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False))
            {
                enabled = e.GetBoolean();
            }
            string? Str(string name) =>
                body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            string? pfxPath = Str("pfxPath");
            string? pfxPassword = Str("pfxPassword");
            string? pemCertPath = Str("pemCertPath");
            string? pemKeyPath = Str("pemKeyPath");
            string? storeName = Str("storeName");
            string? thumbprint = Str("thumbprint");
            CertificateStoreLocationOption? storeLocation = null;
            if (body.TryGetProperty("storeLocation", out var sl) && sl.ValueKind == JsonValueKind.String)
            {
                if (!Enum.TryParse<CertificateStoreLocationOption>(sl.GetString(), ignoreCase: true, out var loc) || !Enum.IsDefined(loc))
                {
                    return UnprocessableEntity(new { error = "storeLocation must be one of: CurrentUser, LocalMachine" });
                }
                storeLocation = loc;
            }
            HttpProtocolsOption? protocols = null;
            if (body.TryGetProperty("protocols", out var pr) && pr.ValueKind == JsonValueKind.String)
            {
                if (!Enum.TryParse<HttpProtocolsOption>(pr.GetString(), ignoreCase: true, out var proto) || !Enum.IsDefined(proto))
                {
                    return UnprocessableEntity(new { error = "protocols must be one of: HTTP1, HTTP2, HTTP1_AND_HTTP2" });
                }
                protocols = proto;
            }
            bool? allowInvalidCertificates = null;
            if (body.TryGetProperty("allowInvalidCertificates", out var aic) && (aic.ValueKind == JsonValueKind.True || aic.ValueKind == JsonValueKind.False))
            {
                allowInvalidCertificates = aic.GetBoolean();
            }
            bool hasAny = enabled.HasValue || pfxPath != null || pfxPassword != null || pemCertPath != null
                || pemKeyPath != null || storeLocation.HasValue || storeName != null || thumbprint != null
                || protocols.HasValue || allowInvalidCertificates.HasValue;
            if (!hasAny)
            {
                return UnprocessableEntity(new { error = "Body contained no recognized HTTPS settings." });
            }
            var (settings, requiresRestart, errors) = _https.ApplyPatch(enabled, pfxPath, pfxPassword,
                pemCertPath, pemKeyPath, storeLocation, storeName, thumbprint, protocols,
                allowInvalidCertificates, _environment.IsProduction());
            if (errors.Count > 0)
            {
                return UnprocessableEntity(new { errors });
            }
            return Ok(new { settings, requiresRestart });
        }
    }
}