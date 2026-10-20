namespace PivotController.Configuration.Models
{
    public enum LogLevelOption { Info, Warn, Error, Fatal }

    /// <summary>General settings per spec §1 (Step 2): port, log level, data folder.</summary>
    public class GeneralSettings
    {
        public const string SectionName = "General";

        public int Port { get; set; } = 9500;
        public LogLevelOption MinimumLogLevel { get; set; } = LogLevelOption.Info;
        public string DataFolder { get; set; } = "DataSource";

        /// <summary>Effective minimum level mapped onto ASP.NET log levels.</summary>
        public static Microsoft.Extensions.Logging.LogLevel ToAspNetLogLevel(LogLevelOption option) => option switch
        {
            LogLevelOption.Fatal => Microsoft.Extensions.Logging.LogLevel.Critical,
            LogLevelOption.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            LogLevelOption.Warn => Microsoft.Extensions.Logging.LogLevel.Warning,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Port < 1 || Port > 65535)
            {
                errors.Add("Port must be between 1 and 65535");
            }
            if (!Enum.IsDefined(MinimumLogLevel))
            {
                errors.Add("MinimumLogLevel must be one of: Info, Warn, Error, Fatal");
            }
            return errors;
        }

        /// <summary>True when DataFolder points outside the application root (path traversal).</summary>
        public static bool IsPathTraversal(string dataFolder, string contentRoot)
        {
            if (string.IsNullOrWhiteSpace(dataFolder))
            {
                return true;
            }
            var resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(contentRoot, dataFolder));
            var root = System.IO.Path.GetFullPath(contentRoot);
            return !resolved.StartsWith(root, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}