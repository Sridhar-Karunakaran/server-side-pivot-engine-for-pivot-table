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
        /// <summary>Enum: Comma (,) or Dot (.). Raw symbols also accepted in JSON.</summary>
        public CsvDecimalSeparator CsvDecimalSeparator { get; set; } = CsvDecimalSeparator.Dot;
        /// <summary>Enum: Comma (,), Dot (.) or Space ( ). Raw symbols also accepted in JSON.</summary>
        public CsvThousandsSeparator CsvThousandsSeparator { get; set; } = CsvThousandsSeparator.Comma;
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
            if (SourceType != DataSourceType.Json && SourceType != DataSourceType.Csv)
            {
                // Database index (spec §3.x): DatabaseType, ConnectionStringName (by reference),
                // and a single read-only SELECT query are mandatory.
                if (DatabaseType == null)
                {
                    errors.Add("DatabaseType is required for database indexes");
                }
                if (string.IsNullOrWhiteSpace(ConnectionStringName))
                {
                    errors.Add("ConnectionStringName is required for database indexes");
                }
                if (string.IsNullOrWhiteSpace(Query))
                {
                    errors.Add("Query is required for database indexes");
                }
                else
                {
                    var trimmed = Query.Trim();
                    if (trimmed.Contains(';'))
                    {
                        errors.Add("Query must be a single read-only SELECT statement");
                    }
                    else if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                             && !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add("Query must be a single read-only SELECT statement");
                    }
                    else
                    {
                        var forbidden = new[] { "INSERT", "UPDATE", "DELETE", "MERGE", "CREATE", "ALTER", "DROP", "GRANT", "TRUNCATE", "REPLACE" };
                        foreach (var kw in forbidden)
                        {
                            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, $"\\b{kw}\\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            {
                                errors.Add("Query must be a single read-only SELECT statement (no DML/DDL)");
                                break;
                            }
                        }
                    }
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
