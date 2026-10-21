using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PivotController.Configuration.Models;
using PivotController.Security.Models;
using PivotController.Security.Services;

namespace PivotController.Security.Controllers
{
    /// <summary>
    /// Security API (spec §5.4, §5.5): user management (create/list/delete), first-user
    /// bootstrap (spec §5.2.5), and GET/PUT /api/config/security (merge-patch).
    /// Passwords are never returned; user lists contain usernames only.
    /// </summary>
    [ApiController]
    [Route("api")]
    public class SecurityController : ControllerBase
    {
        private readonly UserService _users;
        private readonly SecuritySettingsService _security;

        public SecurityController(UserService users, SecuritySettingsService security)
        {
            _users = users;
            _security = security;
        }

        /// <summary>First-user bootstrap (spec §5.2.5): anonymous, only while no users exist.</summary>
        [HttpPost("security/users/bootstrap")]
        public IActionResult Bootstrap([FromBody] JsonElement body)
        {
            if (_users.AnyUsers())
            {
                return Conflict(new { error = "Bootstrap is only available while no users exist. Authenticate and use POST /api/security/users instead." });
            }
            var (username, password, errors) = ParseCredentials(body);
            if (errors.Count > 0) return UnprocessableEntity(new { errors });
            var (user, createErrors) = _users.Bootstrap(username!, password!);
            if (createErrors.Count > 0 || user == null)
            {
                return UnprocessableEntity(new { errors = createErrors });
            }
            return StatusCode(StatusCodes.Status201Created, new { username = user.Username, createdAt = user.CreatedAt });
        }

        /// <summary>Create a user (spec §5.4.1). Requires authentication once any user exists.</summary>
        [HttpPost("security/users")]
        public IActionResult Create([FromBody] JsonElement body)
        {
            var (username, password, errors) = ParseCredentials(body);
            if (errors.Count > 0) return UnprocessableEntity(new { errors });
            var (user, createErrors) = _users.Create(username!, password!);
            if (createErrors.Count > 0 || user == null)
            {
                return UnprocessableEntity(new { errors = createErrors });
            }
            return StatusCode(StatusCodes.Status201Created, new { username = user.Username, createdAt = user.CreatedAt });
        }

        /// <summary>List users (spec §5.4.2): usernames only, no password hashes.</summary>
        [HttpGet("security/users")]
        public IActionResult List() => Ok(new { users = _users.ListUsernames() });

        /// <summary>Delete a user (spec §5.4.3).</summary>
        [HttpDelete("security/users/{username}")]
        public IActionResult Delete(string username)
        {
            return _users.Delete(username) ? NoContent() : NotFound(new { error = $"User '{username}' not found" });
        }

        /// <summary>Get security settings (spec §5.5.1).</summary>
        [HttpGet("config/security")]
        public ActionResult<SecuritySettings> GetSettings() => Ok(_security.Current);

        /// <summary>Merge-patch security settings (spec §5.5.2); applied immediately.</summary>
        [HttpPut("config/security")]
        public IActionResult UpdateSettings([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return UnprocessableEntity(new { error = "Body must be a JSON object." });
            }
            bool? basicAuthEnabled = null;
            if (body.TryGetProperty("basicAuthEnabled", out var bae)
                && (bae.ValueKind == JsonValueKind.True || bae.ValueKind == JsonValueKind.False))
            {
                basicAuthEnabled = bae.GetBoolean();
            }
            List<string>? allowedOrigins = null;
            if (body.TryGetProperty("allowedOrigins", out var ao) && ao.ValueKind == JsonValueKind.Array)
            {
                allowedOrigins = ao.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString() ?? "")
                    .ToList();
            }

            var (settings, errors) = _security.ApplyPatch(basicAuthEnabled, allowedOrigins);
            if (errors.Count > 0)
            {
                return UnprocessableEntity(new { errors });
            }
            return Ok(new { settings, requiresRestart = false });
        }

        private static (string? Username, string? Password, List<string> Errors) ParseCredentials(JsonElement body)
        {
            var errors = new List<string>();
            if (body.ValueKind != JsonValueKind.Object)
            {
                errors.Add("Body must be a JSON object.");
                return (null, null, errors);
            }
            string? username = body.TryGetProperty("username", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            string? password = body.TryGetProperty("password", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(username)) errors.Add("username is required");
            if (string.IsNullOrEmpty(password)) errors.Add("password is required");
            return (username, password, errors);
        }
    }
}