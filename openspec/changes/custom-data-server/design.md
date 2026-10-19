# Custom Data Server Feature - Design

**Date:** 2024-12-19

**Approved By:** Pending User Review

## Design Overview

The Custom Data Server is implemented as an integrated layer within the existing ASP.NET Core PivotController, providing pluggable data connectivity with enterprise-grade security, performance optimization, and configuration management.

## Architectural Design

### Layer Architecture

```
┌──────────────────────────────────────────────────────────┐
│          Presentation Layer (HTTP Endpoints)             │
│              [Existing PivotController]                  │
└──────────────────────┬───────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────┐
│           Request Processing Pipeline                    │
│  ┌────────────────┬───────────┬──────────────────────┐  │
│  │ Authentication │ Request   │ Audit Logging        │  │
│  │ Middleware     │ Routing   │                      │  │
│  └────────────────┴───────────┴──────────────────────┘  │
└──────────────────────┬───────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────┐
│         Business Logic Layer                             │
│  ┌────────────────┬─────────────┬────────────────────┐  │
│  │Configuration   │ Authorization│ Query Optimization │  │
│  │Management      │ & Access    │ Engine            │  │
│  │                │ Control     │                    │  │
│  └────────────────┴─────────────┴────────────────────┘  │
└──────────────────────┬───────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────┐
│         Data Adapter Layer (Pluggable)                   │
│  ┌─────────────┬──────────────┬─────────────────────┐   │
│  │ Abstract    │              │                     │   │
│  │ Data        │ Implementers │ Implementers        │   │
│  │ Adapter     │   (Database) │ (REST API, Files)   │   │
│  └─────────────┴──────────────┴─────────────────────┘   │
└──────────────────────┬───────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────┐
│         External Data Sources                            │
│  • SQL Databases  • REST APIs  • File Systems           │
│  • Cloud Storage  • Other Services                      │
└──────────────────────────────────────────────────────────┘
```

### Configuration Layer Architecture

```
┌────────────────────────────────────┐
│   ConfigurationService             │
│   (Singleton)                      │
│                                    │
│  ┌──────────────────────────────┐ │
│  │ General Settings             │ │
│  │ • Server identifier          │ │
│  │ • Performance tuning         │ │
│  │ • Resource limits            │ │
│  └──────────────────────────────┘ │
│                                    │
│  ┌──────────────────────────────┐ │
│  │ Data Storage Settings        │ │
│  │ • Source configurations      │ │
│  │ • Cache strategies           │ │
│  │ • Failover policies          │ │
│  └──────────────────────────────┘ │
│                                    │
│  ┌──────────────────────────────┐ │
│  │ Database Query Settings      │ │
│  │ • Connection strings         │ │
│  │ • Query timeouts             │ │
│  │ • Result set limits          │ │
│  └──────────────────────────────┘ │
│                                    │
│  ┌──────────────────────────────┐ │
│  │ HTTPS Settings               │ │
│  │ • Certificate configuration  │ │
│  │ • Encryption policies        │ │
│  │ • Certificate validation     │ │
│  └──────────────────────────────┘ │
│                                    │
│  ┌──────────────────────────────┐ │
│  │ Security Settings            │ │
│  │ • Authentication schemes     │ │
│  │ • Authorization policies     │ │
│  │ • Field-level access rules   │ │
│  └──────────────────────────────┘ │
│                                    │
│  ┌──────────────────────────────┐ │
│  │ Report Sharing Settings      │ │
│  │ • Report templates           │ │
│  │ • Distribution rules         │ │
│  │ • Scheduling                 │ │
│  └──────────────────────────────┘ │
└────────────────────────────────────┘
```

### Security & Authorization Flow

```
┌─────────────────────────────────────────────────────┐
│ Client Request (with credentials)                   │
└──────────────────┬──────────────────────────────────┘
                   │
                   ▼
        ┌──────────────────────┐
        │ Authentication       │
        │ Middleware           │
        │ (Validate token/key) │
        └──────────┬───────────┘
                   │
             ┌─────▼─────┐
             │ Valid?    │
             └─────┬─────┘
                   │
    ┌──────────────┴──────────────┐
    │ No                         Yes
    ▼                            ▼
  [401]              ┌─────────────────────┐
  Unauthorized       │ Extract User Role   │
                     │ & Context           │
                     └──────────┬──────────┘
                                │
                                ▼
                     ┌─────────────────────┐
                     │ Authorization Layer │
                     │ Check RBAC Rules    │
                     └──────────┬──────────┘
                                │
                          ┌─────▼─────┐
                          │ Authorized?
                          └─────┬─────┘
                                │
                    ┌───────────┴──────────┐
                    │ No                  Yes
                    ▼                      ▼
                  [403]        ┌───────────────────┐
                Forbidden      │ Field-Level Access│
                               │ Control Check     │
                               └────────┬──────────┘
                                        │
                                        ▼
                        ┌───────────────────────┐
                        │ Apply Field Masking   │
                        │ & Data Filters        │
                        └────────┬──────────────┘
                                 │
                                 ▼
                        ┌───────────────────┐
                        │ Log Access Event  │
                        │ (Audit Trail)     │
                        └────────┬──────────┘
                                 │
                                 ▼
                        ┌───────────────────┐
                        │ Execute Query &   │
                        │ Return Results    │
                        └───────────────────┘
```

## Detailed Component Design

### 1. Configuration Management

**Namespace:** `PivotController.DataServer.Configuration`

#### Classes

```csharp
// General Settings
public class GeneralSettings
{
    public string ServerId { get; set; }
    public string Environment { get; set; }
    public int MaxConcurrentRequests { get; set; }
    public int QueryTimeoutSeconds { get; set; }
    public int MaxResultSetSize { get; set; }
}

// Data Storage Settings
public class DataStorageSettings
{
    public DataSourceConfiguration[] Sources { get; set; }
    public CacheStrategy DefaultCacheStrategy { get; set; }
    public bool EnableIncrementalLoad { get; set; }
    public int CacheExpirationMinutes { get; set; }
}

public class DataSourceConfiguration
{
    public string Name { get; set; }
    public string Type { get; set; } // "Database", "RestAPI", "File", "Cloud"
    public Dictionary<string, string> ConnectionSettings { get; set; }
    public bool IsPrimary { get; set; }
    public string[] FailoverSources { get; set; }
}

// Database Query Settings
public class DatabaseQuerySettings
{
    public Dictionary<string, QueryConfiguration> Queries { get; set; }
    public bool AllowCustomQueries { get; set; }
    public int DefaultPageSize { get; set; }
    public int MaxPageSize { get; set; }
    public int QueryTimeoutSeconds { get; set; }
}

public class QueryConfiguration
{
    public string Name { get; set; }
    public string QueryText { get; set; }
    public string[] AllowedParameters { get; set; }
    public bool RequiresAuthentication { get; set; }
}

// HTTPS Settings
public class HttpsSettings
{
    public bool Enabled { get; set; }
    public string CertificatePath { get; set; }
    public string CertificatePassword { get; set; }
    public bool EnforceCertificateValidation { get; set; }
    public string[] TrustedCertificateThumbprints { get; set; }
}

// Security Settings
public class SecuritySettings
{
    public AuthenticationScheme[] AuthenticationSchemes { get; set; }
    public AuthorizationPolicy[] AuthorizationPolicies { get; set; }
    public FieldLevelAccessRule[] FieldAccessRules { get; set; }
    public bool EnableAuditLogging { get; set; }
    public string AuditLogPath { get; set; }
}

public class AuthenticationScheme
{
    public string Type { get; set; } // "ApiKey", "OAuth2", "Basic", "JWT"
    public Dictionary<string, string> Configuration { get; set; }
}

public class AuthorizationPolicy
{
    public string Role { get; set; }
    public string[] AllowedActions { get; set; }
    public string[] AllowedDataSources { get; set; }
}

public class FieldLevelAccessRule
{
    public string FieldName { get; set; }
    public string[] AllowedRoles { get; set; }
    public bool MaskUnauthorized { get; set; }
    public string MaskValue { get; set; }
}

// Report Sharing Settings
public class ReportSharingSettings
{
    public ReportTemplate[] Templates { get; set; }
    public bool EnableScheduledReports { get; set; }
    public NotificationRule[] NotificationRules { get; set; }
    public ReportAccessPolicy[] AccessPolicies { get; set; }
}

public class ReportTemplate
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public Dictionary<string, object> PivotConfiguration { get; set; }
    public string CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
}

// Configuration Service (Singleton)
public interface ICustomDataServerConfiguration
{
    GeneralSettings GeneralSettings { get; }
    DataStorageSettings DataStorageSettings { get; }
    DatabaseQuerySettings DatabaseQuerySettings { get; }
    HttpsSettings HttpsSettings { get; }
    SecuritySettings SecuritySettings { get; }
    ReportSharingSettings ReportSharingSettings { get; }
    
    void LoadFromAppSettings(IConfiguration config);
    void Validate();
}
```

### 2. Data Adapter Pattern

**Namespace:** `PivotController.DataServer.Adapters`

#### Classes

```csharp
// Query Request/Response
public class DataQueryRequest
{
    public string QueryName { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public string[] FilterFields { get; set; }
    public Dictionary<string, object> FilterValues { get; set; }
    public string[] SortFields { get; set; }
    public bool[] SortDescending { get; set; }
}

public class DataQueryResponse
{
    public object[] Rows { get; set; }
    public int TotalRowCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public DateTime QueryExecutedAt { get; set; }
    public int ExecutionTimeMs { get; set; }
}

// Abstract Data Adapter
public abstract class DataAdapter
{
    protected string Name { get; set; }
    protected Dictionary<string, string> ConnectionSettings { get; set; }
    
    public abstract Task<DataQueryResponse> ExecuteQueryAsync(
        DataQueryRequest request,
        CancellationToken cancellationToken = default);
    
    public abstract Task<Dictionary<string, string>> GetFieldMetadataAsync(
        CancellationToken cancellationToken = default);
    
    public abstract Task<bool> VerifyConnectionAsync(
        CancellationToken cancellationToken = default);
    
    public abstract void Dispose();
}

// Database Adapter Implementation
public class DatabaseAdapter : DataAdapter
{
    private readonly ILogger<DatabaseAdapter> _logger;
    private IDbConnection _connection;
    
    public DatabaseAdapter(
        string name,
        Dictionary<string, string> settings,
        ILogger<DatabaseAdapter> logger)
    {
        Name = name;
        ConnectionSettings = settings;
        _logger = logger;
    }
    
    public override async Task<DataQueryResponse> ExecuteQueryAsync(
        DataQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        // Implementation details in spec.md
    }
    
    public override async Task<Dictionary<string, string>> GetFieldMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        // Implementation details in spec.md
    }
    
    public override async Task<bool> VerifyConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        // Implementation details in spec.md
    }
    
    public override void Dispose()
    {
        _connection?.Dispose();
    }
}

// REST API Adapter Implementation
public class RestApiAdapter : DataAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RestApiAdapter> _logger;
    
    public RestApiAdapter(
        string name,
        Dictionary<string, string> settings,
        HttpClient httpClient,
        ILogger<RestApiAdapter> logger)
    {
        Name = name;
        ConnectionSettings = settings;
        _httpClient = httpClient;
        _logger = logger;
    }
    
    public override async Task<DataQueryResponse> ExecuteQueryAsync(
        DataQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        // Implementation details in spec.md
    }
    
    public override async Task<Dictionary<string, string>> GetFieldMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        // Implementation details in spec.md
    }
    
    public override async Task<bool> VerifyConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        // Implementation details in spec.md
    }
    
    public override void Dispose()
    {
        _httpClient?.Dispose();
    }
}

// Adapter Factory
public interface IDataAdapterFactory
{
    DataAdapter CreateAdapter(DataSourceConfiguration sourceConfig);
}

public class DataAdapterFactory : IDataAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataAdapterFactory> _logger;
    
    public DataAdapterFactory(IServiceProvider serviceProvider, ILogger<DataAdapterFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    public DataAdapter CreateAdapter(DataSourceConfiguration sourceConfig)
    {
        // Implementation details in spec.md
    }
}
```

### 3. Security & Authentication Layer

**Namespace:** `PivotController.DataServer.Security`

#### Classes

```csharp
// Authentication Context
public class DataAccessContext
{
    public string UserId { get; set; }
    public string[] Roles { get; set; }
    public Dictionary<string, object> Claims { get; set; }
    public DateTime AuthenticationTime { get; set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);
}

// Authentication Service
public interface IAuthenticationService
{
    Task<DataAccessContext> AuthenticateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly ICustomDataServerConfiguration _config;
    private readonly ILogger<AuthenticationService> _logger;
    
    public async Task<DataAccessContext> AuthenticateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        // Implementation details in spec.md
    }
}

// Authorization Service
public interface IAuthorizationService
{
    Task<bool> AuthorizeAsync(
        DataAccessContext context,
        string action,
        string dataSource,
        CancellationToken cancellationToken = default);
    
    Task<string[]> GetAccessibleFieldsAsync(
        DataAccessContext context,
        string[] requestedFields,
        CancellationToken cancellationToken = default);
}

public class AuthorizationService : IAuthorizationService
{
    private readonly ICustomDataServerConfiguration _config;
    private readonly ILogger<AuthorizationService> _logger;
    
    public async Task<bool> AuthorizeAsync(
        DataAccessContext context,
        string action,
        string dataSource,
        CancellationToken cancellationToken = default)
    {
        // Implementation details in spec.md
    }
    
    public async Task<string[]> GetAccessibleFieldsAsync(
        DataAccessContext context,
        string[] requestedFields,
        CancellationToken cancellationToken = default)
    {
        // Implementation details in spec.md
    }
}

// Audit Logger
public interface IAuditLogger
{
    Task LogAccessAsync(
        DataAccessContext context,
        string action,
        string resource,
        bool success,
        string details = null);
}

public class AuditLogger : IAuditLogger
{
    private readonly ICustomDataServerConfiguration _config;
    private readonly ILogger<AuditLogger> _logger;
    
    public async Task LogAccessAsync(
        DataAccessContext context,
        string action,
        string resource,
        bool success,
        string details = null)
    {
        // Implementation details in spec.md
    }
}
```

### 4. Integration with PivotController

**Affected File:** `PivotController/Controllers/PivotController.cs`

#### Changes Required

1. Inject new services:
   - `ICustomDataServerConfiguration`
   - `IAuthenticationService`
   - `IAuthorizationService`
   - `IDataAdapterFactory`
   - `IAuditLogger`

2. Add security middleware call in request pipeline

3. Modify `GetData()` method to use data adapters

4. Add new action handlers for custom data server operations

5. Maintain backward compatibility with existing method signatures

## Configuration File Structure

**File:** `appsettings.json` and `appsettings.Development.json`

```json
{
  "CustomDataServer": {
    "GeneralSettings": {
      "ServerId": "pivot-ds-001",
      "Environment": "Development",
      "MaxConcurrentRequests": 100,
      "QueryTimeoutSeconds": 30,
      "MaxResultSetSize": 1000000
    },
    "DataStorageSettings": {
      "Sources": [
        {
          "Name": "PrimarySqlServer",
          "Type": "Database",
          "IsPrimary": true,
          "ConnectionSettings": {
            "Provider": "SqlServer",
            "ConnectionString": "Server=localhost;Database=PivotData;..."
          }
        }
      ],
      "DefaultCacheStrategy": "Memory",
      "EnableIncrementalLoad": true,
      "CacheExpirationMinutes": 60
    },
    "DatabaseQuerySettings": {
      "DefaultPageSize": 1000,
      "MaxPageSize": 10000,
      "QueryTimeoutSeconds": 30,
      "AllowCustomQueries": false
    },
    "HttpsSettings": {
      "Enabled": true,
      "CertificatePath": "/etc/ssl/certs/server.pfx",
      "EnforceCertificateValidation": true
    },
    "SecuritySettings": {
      "AuthenticationSchemes": [
        {
          "Type": "ApiKey",
          "Configuration": {
            "HeaderName": "X-API-Key",
            "ValidKeys": ["key1", "key2"]
          }
        }
      ],
      "EnableAuditLogging": true,
      "AuditLogPath": "/var/log/pivot-audit.log"
    },
    "ReportSharingSettings": {
      "EnableScheduledReports": false
    }
  }
}
```

## Service Registration Pattern

**File:** `Program.cs`

```csharp
// Add Custom Data Server services
builder.Services.AddScoped<ICustomDataServerConfiguration, CustomDataServerConfiguration>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IAuthorizationService, AuthorizationService>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IDataAdapterFactory, DataAdapterFactory>();
builder.Services.AddScoped<IDataServerRequestHandler, DataServerRequestHandler>();

// Add custom data server middleware
app.UseCustomDataServerAuthentication();
app.UseCustomDataServerAuditLogging();
```

## Error Handling Strategy

1. **Authentication Failures:** Return 401 Unauthorized
2. **Authorization Failures:** Return 403 Forbidden
3. **Configuration Errors:** Return 500 with details in development
4. **Query Errors:** Return 400 Bad Request with validation details
5. **Connection Failures:** Return 503 Service Unavailable with retry hint
6. **Timeout:** Return 504 Gateway Timeout

## Testing Strategy

1. **Unit Tests**
   - Configuration validation
   - Authentication and authorization logic
   - Adapter pattern implementations

2. **Integration Tests**
   - Full request pipeline with security
   - Database adapter with test database
   - API adapter with mocked endpoints

3. **Security Tests**
   - SQL injection prevention
   - Unauthorized access attempts
   - Audit logging verification

4. **Performance Tests**
   - Large dataset pagination
   - Query optimization verification
   - Cache effectiveness

## Backward Compatibility

1. Existing pivot endpoint remains unchanged
2. Legacy data source loading methods continue working
3. New features are opt-in via configuration
4. Gradual migration path available

## References

- EXPLORATION.md - Requirements and context
- proposal.md - Feature scope and capabilities
- specs/custom-data-server/spec.md - Detailed technical specifications
