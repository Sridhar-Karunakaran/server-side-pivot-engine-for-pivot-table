using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;
using PivotController.Core.Security;

namespace PivotController.Security
{
    /// <summary>
    /// Basic Authentication middleware (spec §5.2). When Security:Authorization:Enabled,
    /// every /api/* request requires a valid Basic Authorization header matched against
    /// the Data Server credentials (Security:Authorization Username/Password; Admin
    /// Panel later). Password comparison is constant-time.
    /// </summary>
    public class PivotBasicAuthMiddleware
    {
        private const string WWWAuthenticate = "Basic realm=\"pivot-data-server\"";
        private readonly RequestDelegate _next;
        private readonly SecuritySettings _settings;
        private readonly ILogger<PivotBasicAuthMiddleware> _logger;

        public PivotBasicAuthMiddleware(RequestDelegate next, SecuritySettings settings,
            ILogger<PivotBasicAuthMiddleware> logger)
        {
            _next = next;
            _settings = settings;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_settings.IsAuthEnabled || !IsApiPath(context.Request.Path))
            {
                await _next(context);
                return;
            }

            // CORS preflight requests are issued by the browser without credentials;
            // they must pass through so the browser can evaluate the CORS policy.
            if (string.Equals(context.Request.Method, HttpMethods.Options, StringComparison.OrdinalIgnoreCase)
                && context.Request.Headers.ContainsKey("Origin")
                && context.Request.Headers.ContainsKey("Access-Control-Request-Method"))
            {
                await _next(context);
                return;
            }

            var header = context.Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = WWWAuthenticate;
                await context.Response.WriteAsync("{\"error\":\"Unauthorized\"}");
                return;
            }

            var (username, password) = Decode(header["Basic ".Length..].Trim());
            if (username == null
                || !ConstantTimeComparer.TextEquals(username, _settings.Authorization.Username ?? string.Empty)
                || !ConstantTimeComparer.TextEquals(password, _settings.Authorization.Password ?? string.Empty))
            {
                _logger.LogWarning("Authentication failed for user '{User}' from {Remote}.", username, context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = WWWAuthenticate;
                await context.Response.WriteAsync("{\"error\":\"Unauthorized\"}");
                return;
            }

            await _next(context);
        }

        private static bool IsApiPath(PathString path)
            => path.StartsWithSegments("/api");

        private static (string?, string?) Decode(string base64)
        {
            try
            {
                var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                var idx = raw.IndexOf(':');
                return idx < 0 ? (null, null) : (raw[..idx], raw[(idx + 1)..]);
            }
            catch (FormatException)
            {
                return (null, null);
            }
        }
    }
}