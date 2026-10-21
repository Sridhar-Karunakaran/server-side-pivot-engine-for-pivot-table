using Newtonsoft.Json;

namespace PivotController.Configuration.Models
{
    /// <summary>Database Query settings per spec §3.1–§3.3 (Step 7 — Command timeout / Command time limit).</summary>
    public class DatabaseQuerySettings
    {
        /// <summary>Command timeout: enables the Command time limit. When disabled, the Data Server defers
        /// to the SQL driver's own timeout. Database data source type only. Default: disabled.</summary>
        [JsonProperty("commandTimeoutEnabled")]
        public bool CommandTimeoutEnabled { get; set; } = false;

        /// <summary>Command time limit: wait time in seconds before canceling the query attempt and generating
        /// an error. 0 = unlimited. Database data source type only. Default: 150.</summary>
        [JsonProperty("commandTimeLimit")]
        public int CommandTimeLimit { get; set; } = 150;

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (CommandTimeLimit < 0)
            {
                errors.Add("commandTimeLimit must be greater than or equal to 0 (0 = unlimited)");
            }
            return errors;
        }
    }
}
