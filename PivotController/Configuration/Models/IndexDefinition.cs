namespace PivotController.Configuration.Models
{
    public enum DataSourceType { Json, Csv, SqlServer, PostgreSQL, MySql, Oracle, MariaDb, Sqlite }
    public enum DatabaseType { MySql, MariaDB, MSSQL, PostgreSQL, Oracle, Sqlite }
    public enum MappingDataType { Number, String, Date }

    /// <summary>Validation rules for index definitions per spec §0.1, §0.5.</summary>
    public class IndexDefinition
    {
        public string Index { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DataSourceType SourceType { get; set; }
        public string? SourcePath { get; set; }
        public string CsvDelimiter { get; set; } = ",";
        public string CsvDecimalSeparator { get; set; } = ".";
        public string CsvThousandsSeparator { get; set; } = ",";
        public DatabaseType? DatabaseType { get; set; }
        public string? ConnectionStringName { get; set; }
        public string? Query { get; set; }
        public bool AutoReload { get; set; } = false;
        public int ReloadIntervalMinutes { get; set; } = 0;
        /// <summary>Nullable so PUT can distinguish "explicitly false" from "not specified".</summary>
        public bool? MappingEnabled { get; set; }
        public List<IndexFieldMapping>? FieldMappings { get; set; }
        public bool Enabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public static bool IsValidIndexName(string name)
            => !string.IsNullOrEmpty(name)
               && name.Length <= 64
               && System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9._-]+$");

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Index))
            {
                errors.Add("Index name cannot be empty");
            }
            else if (!IsValidIndexName(Index))
            {
                errors.Add("Invalid index name pattern");
            }
            if (SourceType == DataSourceType.Json || SourceType == DataSourceType.Csv)
            {
                if (string.IsNullOrWhiteSpace(SourcePath))
                {
                    errors.Add("SourcePath is required for Json/Csv indexes");
                }
            }
            if (AutoReload && ReloadIntervalMinutes <= 0)
            {
                errors.Add("ReloadIntervalMinutes must be greater than 0 when AutoReload is enabled");
            }
            return errors;
        }
    }

    public class IndexFieldMapping
    {
        public string FieldName { get; set; } = string.Empty;
        public MappingDataType Type { get; set; }
    }

    public enum IndexStatus { Ready, Refreshing, Failed, Disabled }

    public class IndexStatusResponse
    {
        public string Index { get; set; } = string.Empty;
        public IndexStatus Status { get; set; }
        public int RowCount { get; set; }
        public DateTime? LoadedAt { get; set; }
        public string? LastError { get; set; }
    }
}
