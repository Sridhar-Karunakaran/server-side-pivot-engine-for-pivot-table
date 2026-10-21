using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PivotController.Reports.Models
{
    /// <summary>
    /// Server-side wrapper stored in the single report JSON file (spec §6.3):
    /// { "reportId", "name", "owner", "createdAt", "expiresAt", "sizeBytes",
    ///   "report": { ...client-side camelCase dataSourceSettings report... } }.
    /// No separate .meta.json file is written (user direction: one file per report).
    /// </summary>
    public class ReportFile
    {
        [JsonProperty("reportId")]
        public string ReportId { get; set; } = string.Empty;
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        [JsonProperty("owner")]
        public string Owner { get; set; } = string.Empty;
        /// <summary>UTC creation time (ISO 8601).</summary>
        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        /// <summary>UTC expiry time; null = never expires (spec §6.4.1).</summary>
        [JsonProperty("expiresAt")]
        public DateTime? ExpiresAt { get; set; }
        [JsonProperty("sizeBytes")]
        public long SizeBytes { get; set; }
        /// <summary>The client-side report JSON (camelCase dataSourceSettings, no internalProperties).</summary>
        [JsonProperty("report")]
        public JToken? Report { get; set; }
    }
}