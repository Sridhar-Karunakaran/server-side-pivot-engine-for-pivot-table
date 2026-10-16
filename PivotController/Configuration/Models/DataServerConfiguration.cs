namespace PivotController.Configuration.Models;

public enum DataSourceType
{
    Json,
    Csv,
    Database
}

public enum DatabaseType
{
    MySql,
    MariaDB,
    MSSQL,
    PostgreSQL,
    Oracle,
    Sqlite
}

public enum MappingDataType
{
    Number,
    String,
    Date
}

public enum LogLevelOption
{
    Info,
    Warn,
    Error,
    Fatal
}

public enum HttpProtocolsOption
{
    HTTP1,
    HTTP2,
    HTTP1_AND_HTTP2
}

public enum CertificateStoreLocationOption
{
    CurrentUser,
    LocalMachine
}

public class GeneralSettings
{
    public int Port { get; set; } = 9500;
    public LogLevelOption MinimumLogLevel { get; set; } = LogLevelOption.Info;
    public string DataFolder { get; set; } = "DataSource";
}

public class DataStorageSettings
{
    public int RetryAttempts { get; set; } = 3;
    public int CacheSizeLimit { get; set; } = 100;
    public bool KeepDataOnRefresh { get; set; } = true;
    public long MaxCacheItemSizeBytes { get; set; }
}

public class DatabaseQuerySettings
{
    public bool CommandTimeoutEnabled { get; set; }
    public int CommandTimeLimit { get; set; } = 150;
}

public class HttpsSettings
{
    public bool Enabled { get; set; }
    public string? PfxPath { get; set; }
    public string? PfxPassword { get; set; }
    public string? PemCertPath { get; set; }
    public string? PemKeyPath { get; set; }
    public CertificateStoreLocationOption StoreLocation { get; set; } = CertificateStoreLocationOption.CurrentUser;
    public string StoreName { get; set; } = "My";
    public string? Thumbprint { get; set; }
    public HttpProtocolsOption Protocols { get; set; } = HttpProtocolsOption.HTTP1_AND_HTTP2;
    public bool AllowInvalidCertificates { get; set; }
}

public class SecuritySettings
{
    public bool BasicAuthEnabled { get; set; }
    public List<string> AllowedOrigins { get; set; } = ["*"];
}

public class ReportSharingSettings
{
    public bool EndpointEnabled { get; set; }
    public string StoragePath { get; set; } = "DataServer/reports";
    public int ReportExpirationTimeMinutes { get; set; }
    public int MaxFolderSizeMB { get; set; }
    public bool AutoDelete { get; set; }
}

public class IndexFieldMapping
{
    public required string FieldName { get; set; }
    public MappingDataType Type { get; set; }
}

public class IndexDefinition
{
    public required string Index { get; set; }
    public required string Name { get; set; }
    public DataSourceType SourceType { get; set; }
    public string? SourcePath { get; set; }
    public char CsvDelimiter { get; set; } = ',';
    public char CsvDecimalSeparator { get; set; } = '.';
    public char CsvThousandsSeparator { get; set; } = ',';
    public DatabaseType? DatabaseType { get; set; }
    public string? ConnectionStringName { get; set; }
    public string? Query { get; set; }
    public bool AutoReload { get; set; }
    public int ReloadIntervalMinutes { get; set; }
    public bool MappingEnabled { get; set; }
    public List<IndexFieldMapping>? FieldMappings { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class DataServerConfiguration
{
    public const string SectionName = "DataServer";
    public const string CurrentVersion = "1.0";
    public string ConfigVersion { get; set; } = CurrentVersion;
    public string DefaultIndexId { get; set; } = "default";
    public GeneralSettings General { get; set; } = new();
    public DataStorageSettings DataStorage { get; set; } = new();
    public DatabaseQuerySettings DatabaseQuery { get; set; } = new();
    public HttpsSettings Https { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public ReportSharingSettings ReportSharing { get; set; } = new();
    public List<IndexDefinition> Indexes { get; set; } = [];
}
