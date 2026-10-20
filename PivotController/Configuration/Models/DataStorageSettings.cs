namespace PivotController.Configuration.Models
{
    /// <summary>Data storage settings per spec §2.1 (Step 3): caching and retry behavior.</summary>
    public class DataStorageSettings
    {
        public const string SectionName = "DataStorage";

        /// <summary>Retry attempts for transient load failures (0–30). Exponential backoff.</summary>
        public int RetryAttempts { get; set; } = 3;

        /// <summary>Maximum number of cached index datasets. 0 disables caching (LRU eviction).</summary>
        public int CacheSizeLimit { get; set; } = 100;

        /// <summary>Retain old data (and Ready status) when a reload fails.</summary>
        public bool KeepDataOnRefresh { get; set; } = true;

        /// <summary>Maximum per-index cache entry size in bytes. 0 = unlimited; oversize data is not cached.</summary>
        public long MaxCacheItemSizeBytes { get; set; } = 0;

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (RetryAttempts < 0 || RetryAttempts > 30)
            {
                errors.Add("RetryAttempts must be between 0 and 30");
            }
            if (CacheSizeLimit < 0)
            {
                errors.Add("CacheSizeLimit must be >= 0 (0 disables caching)");
            }
            if (MaxCacheItemSizeBytes < 0)
            {
                errors.Add("MaxCacheItemSizeBytes must be >= 0 (0 = unlimited)");
            }
            return errors;
        }
    }
}