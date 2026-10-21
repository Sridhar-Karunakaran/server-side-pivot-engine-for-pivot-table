namespace PivotController.Configuration.Models
{
    /// <summary>
    /// Security settings (spec §5, clarified schema):
    /// Security: { Authorization: { Enabled }, CORS: { AllowOrigin } }.
    /// Credentials (username/password) are created in the Data Server configuration
    /// (appsettings.json) for now; they will be obtained from the Admin Panel later.
    /// </summary>
    public class SecuritySettings
    {
        public const string SectionName = "Security";

        public SecurityAuthorizationSettings Authorization { get; set; } = new();

        public SecurityCorsSettings CORS { get; set; } = new();

        public bool IsAuthEnabled => Authorization?.Enabled == true
            && !string.IsNullOrWhiteSpace(Authorization.Username);

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Authorization == null)
            {
                errors.Add("Security:Authorization is required");
                return errors;
            }
            if (Authorization.Enabled)
            {
                if (string.IsNullOrWhiteSpace(Authorization.Username))
                {
                    errors.Add("Security:Authorization:Username is required when Authorization is enabled");
                }
                if (string.IsNullOrWhiteSpace(Authorization.Password))
                {
                    errors.Add("Security:Authorization:Password is required when Authorization is enabled");
                }
                // Flexmonster UG rule: when basic authorization is enabled, Allow origin must
                // define specific origin URLs — never "*" (allow all) and never empty.
                if (CORS == null || string.IsNullOrWhiteSpace(CORS.AllowOrigin))
                {
                    errors.Add("Security:CORS:AllowOrigin must be specified when Authorization is enabled");
                }
                else if (CORS.AllowsAllOrigins)
                {
                    errors.Add("Security:CORS:AllowOrigin must not be '*' (allow all origins) when Authorization is enabled; specify specific origins, separated by commas");
                }
                else if (CORS.Origins.Any(o => !SecurityCorsSettings.IsProperOrigin(o)))
                {
                    errors.Add("Security:CORS:AllowOrigin must be proper origin URL(s) (http:// or https://), separated by commas, when Authorization is enabled");
                }
            }
            if (CORS == null)
            {
                errors.Add("Security:CORS is required");
            }
            else if (string.IsNullOrWhiteSpace(CORS.AllowOrigin))
            {
                errors.Add("Security:CORS:AllowOrigin must be a URL, '*' or a comma-separated origin list");
            }
            return errors;
        }

        public SecuritySettings Clone() => new()
        {
            Authorization = new SecurityAuthorizationSettings
            {
                Enabled = Authorization?.Enabled ?? false,
                Username = Authorization?.Username,
                Password = Authorization?.Password
            },
            CORS = new SecurityCorsSettings { AllowOrigin = CORS?.AllowOrigin ?? "*" }
        };

        /// <summary>Sanitized view for API responses — password is never returned.</summary>
        public SecuritySettings Sanitized() => new()
        {
            Authorization = new SecurityAuthorizationSettings
            {
                Enabled = Authorization?.Enabled ?? false,
                Username = Authorization?.Username
            },
            CORS = new SecurityCorsSettings { AllowOrigin = CORS?.AllowOrigin ?? "*" }
        };
    }

    public class SecurityAuthorizationSettings
    {
        public bool Enabled { get; set; } = false;

        /// <summary>Data Server credential (Admin Panel later).</summary>
        public string? Username { get; set; }

        /// <summary>Data Server credential (Admin Panel later).</summary>
        public string? Password { get; set; }
    }

    public class SecurityCorsSettings
    {
        public string AllowOrigin { get; set; } = "*";

        public bool AllowsAllOrigins => AllowOrigin == "*";

        /// <summary>
        /// Configured origins, comma-separated (Flexmonster UG: "To specify several
        /// origins, separate them with a comma"). Whitespace is trimmed per entry.
        /// </summary>
        public string[] Origins => AllowsAllOrigins
            ? Array.Empty<string>()
            : AllowOrigin.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>
        /// Validates a single origin entry (Flexmonster UG rule): when basic authorization
        /// is enabled, Allow origin must specify proper origin URLs — never "*" (allow all).
        /// </summary>
        public static bool IsProperOrigin(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin) || origin.Trim() == "*")
            {
                return false;
            }
            if (!Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }
            // Strict origin check: only http/https URLs with a host; Uri.TryCreate accepts
            // things like "not a url" (implicit file path) or "C:\path" that are not origins.
            return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrWhiteSpace(uri.Host);
        }
    }
}