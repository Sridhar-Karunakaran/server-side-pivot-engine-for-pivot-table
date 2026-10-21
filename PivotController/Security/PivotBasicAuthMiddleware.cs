using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;
using PivotController.Security.Services;

namespace PivotController.Security
{
    /// <summary>
    /// Basic Authentication middleware (spec §5.2). When BasicAuthEnabled, ALL
    /// administrative endpoints (/api/config/*, /api/indexes/*, /api/security/*) and the
    /// pivot data endpoint (/api/pivot/post) require a valid Authorization header. The
    /// ONLY anonymous-capable route is POST /api/security/users/bootstrap, and only
    /// while no users exist (spec §5.2.5 bootstrap flow).
    /// </summary>
    public class PivotBasicAuthMiddleware
    {
        private const string WWWAuthenticate = "Basic realm=\"pivot-data-server\"";
        private readonly RequestDelegate _next;
        private readonly SecuritySettings _settings;
        private readonly UserService _users;
        private readonly ILogger<PivotBasicAuthMiddleware> _logger;

        public PivotBasicAuthMiddleware(RequestDelegate next, SecuritySettings settings,
            UserService users, ILogger<PivotBasicAuthMiddleware> logger)
        {
            _next = next;
            _settings = settings;
            _users = users;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_settings.BasicAuthEnabled || !IsProtectedPath(context.Request.Path))
            {
                await _next(context);
                return;
            }

            // Bootstrap exception (spec §5.2.5): anonymous only while no users exist.
            if (IsBootstrapPath(context.Request) && !_users.AnyUsers())
            {
                await _next(context);
                return;
            }

            var header = context.Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = WWWAuthenticate;
                return;
            }

            string username, password;
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(header["Basic ".Length..].Trim()));
                var sep = decoded.IndexOf(':');
                if (sep < 0)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = WWWAuthenticate;
                    return;
                }
                username = decoded[..sep];
                password = decoded[(sep + 1)..];
            }
            catch (FormatException)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = WWWAuthenticate;
                return;
            }

            if (_users.VerifyCredentials(username, password) == null)
            {
                _logger.LogWarning("Authentication failed for user '{Username}' from {RemoteIp}.", username, context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = WWWAuthenticate;
                return;
            }

            await _next(context);
        }

        private static bool IsProtectedPath(PathString path)
        {
            return path.StartsWithSegments("/api");
        }

        private static bool IsBootstrapPath(HttpRequest request)
        {
            return request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                && request.Path.StartsWithSegments("/api/security/users/bootstrap");
        }
    }
}