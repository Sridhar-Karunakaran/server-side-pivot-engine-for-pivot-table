namespace PivotController.Configuration.Models
{
    /// <summary>Cache observability snapshot (spec §2.3 testing aid): limit vs cached entries.</summary>
    public class CacheStatsResponse
    {
        /// <summary>Configured maximum number of cached index datasets (0 = disabled).</summary>
        public int CacheSizeLimit { get; set; }
        /// <summary>Number of index datasets currently cached.</summary>
        public int CachedCount { get; set; }
        /// <summary>Registered indexes currently cached (data:{index} entries present).</summary>
        public List<string> CachedIndexes { get; set; } = new();
        /// <summary>Registered indexes whose data entry is NOT cached (evicted or never loaded).</summary>
        public List<string> EvictedIndexes { get; set; } = new();
    }
}