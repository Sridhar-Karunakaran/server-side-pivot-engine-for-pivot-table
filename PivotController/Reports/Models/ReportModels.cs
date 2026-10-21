using Newtonsoft.Json;

namespace PivotController.Reports.Models
{
    /// <summary>Report metadata returned by the list API (spec §6.3.3); payload excluded.</summary>
    public class ReportMetadata
    {
        public string ReportId { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Owner { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public long SizeBytes { get; set; }
    }

    /// <summary>Persisted report file: metadata + payload, stored as one JSON file per report.</summary>
    public class StoredReport
    {
        public string ReportId { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string? Owner { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
        public long SizeBytes { get; set; }

        /// <summary>The pivot report payload (rows/columns/values/measures/filters...).</summary>
        [JsonProperty("payload")]
        public object? Payload { get; set; }
    }
}