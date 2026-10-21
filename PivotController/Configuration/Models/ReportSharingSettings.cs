namespace PivotController.Configuration.Models
{
    /// <summary>
    /// Report Sharing settings per spec §6 (Step 6): endpoint toggle, storage folder,
    /// expiration, quota and auto-delete eviction. Configured in appsettings.json under
    /// "ReportSharingOptions"; persisted changes written to {DataFolder}/reportsharing.json.
    /// </summary>
    public class ReportSharingSettings
    {
        public const string SectionName = "ReportSharingOptions";

        /// <summary>Enables the report sharing endpoints. Default disabled (404 on report APIs).</summary>
        public bool EndpointEnabled { get; set; } = false;

        /// <summary>Folder where report files are stored. Relative paths resolve against {DataFolder}.</summary>
        public string PathToFolder { get; set; } = "DataServer/reports";

        /// <summary>Report retention in minutes. 0 = reports never expire.</summary>
        public int ReportExpirationTime { get; set; } = 0;

        /// <summary>Maximum report folder size in MB. 0 = unlimited.</summary>
        public int MaxSizeOfFolder { get; set; } = 0;

        /// <summary>
        /// Evicts oldest reports when MaxSizeOfFolder is reached. Governs ONLY quota
        /// eviction — expiration cleanup runs regardless of this flag (spec §6.4/§6.6).
        /// </summary>
        public bool AutoDeletingEnabled { get; set; } = false;

        public ReportSharingSettings Clone() => new()
        {
            EndpointEnabled = EndpointEnabled,
            PathToFolder = PathToFolder,
            ReportExpirationTime = ReportExpirationTime,
            MaxSizeOfFolder = MaxSizeOfFolder,
            AutoDeletingEnabled = AutoDeletingEnabled
        };

        /// <summary>True when PathToFolder escapes the given root (path traversal).</summary>
        public static bool IsPathTraversal(string pathToFolder, string root)
        {
            if (string.IsNullOrWhiteSpace(pathToFolder))
            {
                return false; // empty falls back to the default folder under the root
            }
            if (System.IO.Path.IsPathRooted(pathToFolder))
            {
                return false; // absolute paths are explicitly allowed (e.g. D:\Reports)
            }
            var resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, pathToFolder));
            var rootFull = System.IO.Path.GetFullPath(root);
            return !resolved.StartsWith(rootFull, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validates persisted/patched settings. Traversal checking against the DataFolder root
        /// is done by the caller (it needs the General settings root path).
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (ReportExpirationTime < 0)
            {
                errors.Add("ReportExpirationTime must be >= 0 (minutes; 0 = never expire)");
            }
            if (MaxSizeOfFolder < 0)
            {
                errors.Add("MaxSizeOfFolder must be >= 0 (MB; 0 = unlimited)");
            }
            if (PathToFolder != null && PathToFolder.Contains(".."))
            {
                errors.Add("PathToFolder path traversal (..) not allowed");
            }
            return errors;
        }
    }
}