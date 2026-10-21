namespace PivotController.Reports.Models
{
    /// <summary>Sidecar metadata for a stored report (spec §6.3): {reportId}.meta.json.</summary>
    public class ReportMetadata
    {
        public string ReportId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        /// <summary>UTC creation time (ISO 8601).</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        /// <summary>UTC expiry time; null = never expires (spec §6.4.1).</summary>
        public DateTime? ExpiresAt { get; set; }
        public long SizeBytes { get; set; }
    }
}