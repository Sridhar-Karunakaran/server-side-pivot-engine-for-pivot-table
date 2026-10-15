# Flexmonster Data Server Integration - Phase 1 Technical Specification

## Document Information
- Version: 1.0
- Phase: Phase 1 – Backend Infrastructure
- Scope: Server-Side Controller and Configuration Management
- Out of Scope: Admin Panel UI, Monitoring UI, Diagnostics UI

---

# 1. Purpose

This document defines the technical specification for implementing a Flexmonster Data Server-compatible backend controller that supports configuration-driven indexing, security, HTTPS, report sharing, logging, and data connectivity.

The purpose of Phase 1 is to provide all backend functionality required by the Flexmonster Admin Panel without implementing the Admin Panel itself.

---

# 2. Objectives

The server-side controller shall:

- Manage server configuration.
- Manage indexes.
- Support JSON, CSV, and Database sources.
- Support HTTPS configuration.
- Support Basic Authentication.
- Support CORS.
- Support Report Sharing.
- Support Logging.
- Persist configurations.
- Expose APIs for future Admin Panel integration.

---

# 3. High-Level Architecture

```text
+-------------------------+
| Flexmonster Pivot Table |
+-------------------------+
            |
            v
+-------------------------+
| Data Server Controller  |
+-------------------------+
| Configuration Manager   |
| Index Manager           |
| Security Manager        |
| HTTPS Manager           |
| Report Share Manager    |
| Database Query Manager  |
| Logging Manager         |
+-------------------------+
            |
    +-------+-------+
    |               |
    v               v
 JSON/CSV       Databases
```

---

# 4. Controller Responsibilities

## Configuration Manager

Responsible for:

- Loading configurations.
- Applying configurations.
- Validating configurations.
- Persisting configurations.
- Serving configuration APIs.

## Index Manager

Responsible for:

- Creating indexes.
- Reloading indexes.
- Deleting indexes.
- Managing cache.
- Retry processing.

## Security Manager

Responsible for:

- User management.
- Basic Authentication.
- Origin validation.

## HTTPS Manager

Responsible for:

- Certificate loading.
- SSL configuration.
- Protocol configuration.

## Report Sharing Manager

Responsible for:

- Saving reports.
- Retrieving reports.
- Expiring reports.
- Folder maintenance.

---

# 5. Supported Configuration Sections

## 5.1 General Settings

### Configuration Model

```csharp
public class GeneralSettings
{
    public int Port { get; set; } = 9500;
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Info;
}
```

### Properties

- Port (Default: 9500)
- Minimum Log Level (Default: INFO)

### Valid Log Levels

- INFO
- WARN
- ERROR
- FATAL

### Validation Rules

- Port range: 1-65535

---

## 5.2 Data Storage Settings

### Configuration Model

```csharp
public class DataStorageSettings
{
    public int RetryAttempts { get; set; } = 3;
    public int CacheSizeLimit { get; set; } = 100;
    public bool KeepDataOnRefresh { get; set; } = true;
}
```

### Properties

| Property | Default |
|----------|----------|
| RetryAttempts | 3 |
| CacheSizeLimit | 100 |
| KeepDataOnRefresh | true |

### Validation

- RetryAttempts: 0-30
- CacheSizeLimit >= 0

---

## 5.3 Database Query Settings

### Configuration Model

```csharp
public class DatabaseQuerySettings
{
    public bool CommandTimeoutEnabled { get; set; }
    public int CommandTimeLimit { get; set; } = 150;
}
```

### Defaults

- Command Timeout: Disabled
- Command Time Limit: 150 seconds

### Validation

- 0 = Unlimited
- Minimum value = 0

### Applies To

- Initial index creation
- Index reload
- Database query execution
- Database reconnection

---

## 5.4 HTTPS Settings

### Supported Features

- HTTPS Enable/Disable
- PFX Certificates
- PEM Certificates
- Subject Store Certificates
- HTTP1
- HTTP2

### Default Values

| Property | Default |
|----------|----------|
| HTTPS | Disabled |
| Protocols | HTTP1_AND_HTTP2 |
| AllowInvalid | false |
| Store Location | CurrentUser |

---

## 5.5 Security Settings

### Configuration Model

```csharp
public class SecuritySettings
{
    public bool BasicAuthEnabled { get; set; }
    public List<string> AllowedOrigins { get; set; }
}
```

### Defaults

- BasicAuthEnabled = false
- AllowOrigins = *

### Supported Features

- Basic Authentication
- User Management
- CORS Validation

---

## 5.6 Report Sharing Settings

### Configuration Model

```csharp
public class ReportSharingSettings
{
    public bool EndpointEnabled { get; set; }
    public string StoragePath { get; set; }
    public int ReportExpirationTimeMinutes { get; set; }
    public int MaxFolderSizeMB { get; set; }
    public bool AutoDelete { get; set; }
}
```

### Defaults

| Property | Default |
|----------|----------|
| EndpointEnabled | false |
| Expiration | 0 |
| MaxFolderSize | 0 |
| AutoDelete | false |

### Notes

- Expiration=0 means permanent storage.
- MaxFolderSize=0 means unlimited storage.

---

## 5.7 Index Configuration

### Configuration Model

```csharp
public class IndexDefinition
{
    public string Id { get; set; }
    public string Name { get; set; }
    public DataSourceType SourceType { get; set; }
    public string SourcePath { get; set; }
    public string ConnectionString { get; set; }
    public string Query { get; set; }
    public bool AutoReload { get; set; }
}
```

### Supported Sources

- JSON
- CSV
- SQL Server
- PostgreSQL
- MySQL
- Oracle
- MariaDB

---

# 6. User Management

### User Model

```csharp
public class User
{
    public string Username { get; set; }
    public string PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### Rules

- Username must be unique.
- Username cannot be modified.
- Passwords must be hashed.
- Plain-text passwords must never be stored.

Recommended hashing:

- BCrypt
- PBKDF2
- Argon2

---

# 7. REST API Specification

## Configuration APIs

### Get Configuration

```http
GET /api/config
```

### Get Configuration Section

```http
GET /api/config/general
GET /api/config/security
GET /api/config/https
```

### Update Configuration

```http
PUT /api/config
```

---

## User Management APIs

### Create User

```http
POST /api/security/users
```

### Update User

```http
PUT /api/security/users/{username}
```

### Delete User

```http
DELETE /api/security/users/{username}
```

### Get Users

```http
GET /api/security/users
```

---

## Index APIs

### Create Index

```http
POST /api/indexes
```

### Get Indexes

```http
GET /api/indexes
```

### Reload Index

```http
POST /api/indexes/{id}/reload
```

### Delete Index

```http
DELETE /api/indexes/{id}
```

---

# 8. Persistence Requirements

## Configuration Storage

Recommended:

```text
config.json
```

Structure:

```json
{
  "GeneralSettings": {},
  "DataStorageSettings": {},
  "DatabaseQuerySettings": {},
  "SecuritySettings": {},
  "HttpsSettings": {},
  "ReportSharingSettings": {}
}
```

## User Storage

Options:

- users.json
- Database Table

## Index Storage

Options:

- indexes.json
- Database Table

---

# 9. Logging Requirements

Supported Levels:

- INFO
- WARN
- ERROR
- FATAL

Supported Targets:

- Console
- File

Categories:

- Configuration
- Authentication
- Indexing
- Database Queries
- Report Sharing
- HTTPS

---

# 10. Acceptance Criteria

## General Settings

- Port is configurable.
- Log level filtering works.

## Data Storage

- Retry logic works.
- Cache size is enforced.
- KeepDataOnRefresh is supported.

## Database Query Settings

- Timeout is enforced.
- Unlimited mode is supported.

## HTTPS

- PFX certificates supported.
- PEM certificates supported.
- Subject Store supported.

## Security

- Basic Authentication supported.
- User CRUD supported.
- Origin validation supported.

## Report Sharing

- Reports can be stored.
- Expiration rules work.
- Folder limits are enforced.
- Auto-delete works.

## Index Management

- Create index.
- Reload index.
- Delete index.
- Persist index metadata.

---

# 11. Phase 1 Deliverables

## Included

- Configuration Models
- Configuration APIs
- User Management APIs
- Index Management APIs
- HTTPS Configuration
- Security Configuration
- Report Sharing Configuration
- Persistence Layer
- Logging Infrastructure

## Excluded

- Admin Panel UI
- Monitoring UI
- Diagnostics UI
- Runtime Visual Configuration Screen

---

# 12. Future Phase Considerations

Phase 2 may introduce:

- Admin Panel UI
- Monitoring Dashboard
- Diagnostics Dashboard
- Runtime Configuration Editor
- Performance Metrics Viewer
- Health Monitoring

---

# Conclusion

Phase 1 establishes a backend-first architecture that fully supports Flexmonster Data Server configuration management through APIs and persisted configuration models. The design intentionally separates backend functionality from UI concerns to allow future implementation of an Admin Panel without requiring architectural changes.
