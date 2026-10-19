namespace PivotController.Configuration.Models
{
    /// <summary>Root configuration bound from the DataServer section of appsettings.json.</summary>
    public class DataServerSettings
    {
        public const string SectionName = "DataServer";

        /// <summary>Index used when a pivot request carries no index (backward compatible).</summary>
        public string DefaultIndexId { get; set; } = "default";

        public bool KeepDataOnRefresh { get; set; } = true;

        public int RetryAttempts { get; set; } = 3;

        public List<IndexDefinition> Indexes { get; set; } = new();
    }
}
