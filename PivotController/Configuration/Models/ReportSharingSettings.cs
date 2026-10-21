namespace PivotController.Configuration.Models
{
    /// <summary>
    /// Report Sharing settings (spec §6). StoragePath resolution (clarified):
    /// null → {DataFolder}/reports; relative → under {DataFolder}; absolute → used as-is.
    /// </summary>
    public class ReportSharingSettings
    {
        public const string SectionName = "ReportSharing";

        public bool EndpointEnabled { get; set; } = false;

        /// <summary>Null → {DataFolder}/reports; relative resolved under {DataFolder}; absolute used directly.</summary>
        public string? StoragePath { get; set; }

        public int ReportExpirationTimeMinutes { get; set; } = 0;  // 0 = permanent

        public int MaxFolderSizeMB { get; set; } = 0;             // 0 = unlimited

        public bool AutoDelete { get; set; } = false;

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (ReportExpirationTimeMinutes < 0) errors.Add("ReportExpirationTimeMinutes must be >= 0");
            if (MaxFolderSizeMB < 0) errors.Add("MaxFolderSizeMB must be >= 0");
            return errors;
        }

        /// <summary>
        /// Resolves the effective storage folder (clarified §6.1): null → {DataFolder}/reports;
        /// relative path → {DataFolder}/{relative}; absolute → used directly. Rejects traversal.
        /// </summary>
        public string ResolveStorageFolder(string dataFolderRoot, string contentRoot)
        {
            if (string.IsNullOrWhiteSpace(StoragePath))
            {
                return Path.Combine(dataFolderRoot, "reports");
            }
            if (Path.IsPathRooted(StoragePath))
            {
                return StoragePath;
            }
            var combined = Path.GetFullPath(Path.Combine(dataFolderRoot, StoragePath));
            if (!combined.StartsWith(Path.GetFullPath(dataFolderRoot), StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Report StoragePath path traversal not allowed");
            }
            return combined;
        }

        public ReportSharingSettings Clone() => new()
        {
            EndpointEnabled = EndpointEnabled,
            StoragePath = StoragePath,
            ReportExpirationTimeMinutes = ReportExpirationTimeMinutes,
            MaxFolderSizeMB = MaxFolderSizeMB,
            AutoDelete = AutoDelete
        };
    }
}