namespace PivotController.Configuration.Models
{
    /// <summary>Security settings (spec §5): Basic Auth toggle and allowed CORS origins.</summary>
    public class SecuritySettings
    {
        public const string SectionName = "Security";

        public bool BasicAuthEnabled { get; set; } = false;

        public List<string> AllowedOrigins { get; set; } = new() { "*" };

        public bool AllowsAllOrigins => AllowedOrigins.Count == 0
            || AllowedOrigins.Any(o => o == "*");

        public bool IsOriginAllowed(string? origin)
        {
            if (AllowsAllOrigins) return true;
            if (string.IsNullOrWhiteSpace(origin)) return true; // non-browser clients are not subject to CORS
            return AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (AllowedOrigins == null)
            {
                errors.Add("AllowedOrigins must be a list of origins");
            }
            else if (AllowedOrigins.Any(o => string.IsNullOrWhiteSpace(o)))
            {
                errors.Add("AllowedOrigins entries must be non-empty");
            }
            return errors;
        }

        public SecuritySettings Clone() => new()
        {
            BasicAuthEnabled = BasicAuthEnabled,
            AllowedOrigins = AllowedOrigins == null ? new() { "*" } : new List<string>(AllowedOrigins)
        };
    }
}