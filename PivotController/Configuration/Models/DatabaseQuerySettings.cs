namespace PivotController.Configuration.Models
{
    /// <summary>Database Query settings (spec §3): command timeout for database-backed indexes.</summary>
    public class DatabaseQuerySettings
    {
        public const string SectionName = "DatabaseQuery";

        public bool CommandTimeoutEnabled { get; set; } = false;

        public int CommandTimeLimit { get; set; } = 150;  // seconds

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (CommandTimeLimit < 0) errors.Add("CommandTimeLimit must be >= 0");
            return errors;
        }

        public DatabaseQuerySettings Clone() => new()
        {
            CommandTimeoutEnabled = CommandTimeoutEnabled,
            CommandTimeLimit = CommandTimeLimit
        };
    }
}