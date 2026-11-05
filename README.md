# Server-Side Pivot Engine

This project demonstrates a Syncfusion EJ2 Pivot Table connected to a server-side
pivot engine. The browser sends the Pivot Table report and user actions to the
ASP.NET Core server. The server loads data from a configured index, performs
pivoting and aggregation, and returns the result to the client. The browser does
not need to download the entire data source just to render the pivot view.

The **Custom Data Server** is the optional server configuration layer: it lets
you define data sources and control indexing, storage, database timeouts,
security, HTTPS, and report sharing in `PivotController/appsettings.json`.
Configure only the sections your deployment needs.

## Contents

- [How the server-side pivot works](#how-the-server-side-pivot-works)
- [Key capabilities](#key-capabilities)
- [Requirements and quick start](#requirements-and-quick-start)
- [Project structure](#project-structure)
- [Connect the sample client](#connect-the-sample-client)
- [Configure the Custom Data Server](#configure-the-custom-data-server)
  - [1. Define an index](#1-define-an-index)
  - [2. Configure general server settings](#2-configure-general-server-settings)
  - [3. Configure data storage and caching](#3-configure-data-storage-and-caching)
  - [4. Configure database query timeouts](#4-configure-database-query-timeouts)
  - [5. Configure security and browser origins](#5-configure-security-and-browser-origins)
  - [6. Configure HTTPS](#6-configure-https)
  - [7. Configure report sharing](#7-configure-report-sharing)
  - [8. Configure ASP.NET Core host logging](#8-configure-aspnet-core-host-logging)
- [Configuration persistence and APIs](#configuration-persistence-and-apis)
- [Troubleshooting](#troubleshooting)

## How the server-side pivot works

The request flow is:

1. The client Pivot Table sends its report settings and requested action to
   `POST /api/pivot/post`.
2. The server selects an index, loads its data source, and applies its field
   mappings.
3. The Syncfusion Pivot Engine processes the report on the server.
4. The server returns the pivot result to the browser. Actions such as field
   member retrieval, drill-through, and export are also handled by this endpoint.

The client and server have separate responsibilities:

| Part | Responsibility |
|---|---|
| Client Pivot Table | Displays the result and sends the report layout, filters, and user actions. |
| PivotController | Receives the Pivot Table request and routes it to the selected index. |
| Custom Data Server | Defines indexes and controls how data is loaded, cached, protected, and served. |
| Pivot Engine | Performs server-side pivot calculations and returns the response. |

Drill-through can request relevant source records; the regular pivot response is
the server-computed pivot result rather than an initial download of the complete
dataset.

## Key capabilities

- Server-side Pivot Engine processing through `POST /api/pivot/post`.
- JSON, CSV, PostgreSQL, and SQLite data indexes.
- Per-index reload, field mapping, enable/disable, and status management.
- Configurable retry behavior, response caching, and database query timeouts.
- Optional Basic Authentication, CORS restrictions, HTTPS, and report sharing.

## Requirements and quick start

- .NET 10 SDK
- Python 3 (only needed to serve the static browser sample), or another static
  web server
- A configured JSON/CSV data file, or a PostgreSQL/SQLite database
- Internet access for the Syncfusion EJ2 assets referenced by the sample page

From the repository root, start the server:

```powershell
dotnet restore PivotController\PivotController.csproj
dotnet run --project PivotController\PivotController.csproj
```

In a second terminal, serve the client sample:

```powershell
python -m http.server 5500 --directory Sample
```

Open `http://localhost:5500/pivot.html`. The sample server listens on port
`8765` by default; the client calls `http://localhost:8765/api/pivot/post`.
Because the browser page and API use different ports, the sample CORS setting
allows the local page to call the API.

To verify the server has loaded the configured indexes, open
`http://localhost:8765/api/indexes`. The response should include the enabled
`Sales-Json` index and its status.

## Project structure

```text
PivotController/
  Controllers/                 Pivot endpoint
  Configuration/               Indexes, settings, and configuration APIs
  Data/Providers/              JSON, CSV, PostgreSQL, and SQLite providers
  Indexing/                    Index lifecycle and response caching
  DataSource/                  Sample JSON and CSV files
  appsettings.json             Default server and data-index configuration
Sample/
  pivot.html                   Browser sample page
  pivot.js                     Server-mode Pivot Table setup
README.md                      This guide
```

## Connect the sample client

The example client files are `Sample/pivot.html` and `Sample/pivot.js`. The
essential server-mode settings are:

```javascript
var pivotGridObj = new ej.pivotview.PivotView({
    dataSourceSettings: {
        url: 'http://localhost:8765/api/pivot/post',
        mode: 'Server',
        type: 'JSON',
        enableSorting: true
    },
    enableVirtualization: true,
    enableValueSorting: true,
    allowCalculatedField: true,
    showGroupingBar: true,
    showFieldList: true,
    height: 500,
    width: 1000
});
pivotGridObj.appendTo('#PivotView');
```

This matches the current sample: the URL points to the server endpoint and no
index name is set in the client. The server therefore selects
`DataServer:DefaultIndexId`. The Field List lets users arrange the available
fields. To define an initial report in code, add field names that exist in the
selected index, for example:

```javascript
rows: [{ name: 'Sector', caption: 'Sector' }],
columns: [{ name: 'EnerType', caption: 'Energy Type' }],
values: [
    { name: 'PowUnits', caption: 'Power Units' },
    { name: 'ProCost', caption: 'Production Cost' }
]
```

Those fields are present in the sample `Sales-Json` data. If a browser page is
served from the same origin as the API, use a relative endpoint such as
`/api/pivot/post`. If the page and API have different origins, configure
`Security:CORS:AllowOrigin` for the page's origin as described below.

## Configure the Custom Data Server

Edit `PivotController/appsettings.json`. The following sections are explained
in the order you would typically configure them. JSON examples are snippets to
merge into that file; keep its other ASP.NET Core settings as needed.

### 1. Define an index

An index connects a name used by the server to a file or database source. Start
with an index for the included JSON sample:

```json
"DataServer": {
  "DefaultIndexId": "Sales-Json",
  "Indexes": [
    {
      "Index": "Sales-Json",
      "Name": "Sales analysis",
      "SourceType": "Json",
      "SourcePath": "sales-analysis.json",
      "Enabled": true,
      "AutoReload": true,
      "ReloadIntervalMinutes": 20,
      "MappingEnabled": false,
      "FieldMappings": []
    }
  ]
}
```

`SourcePath` is relative to `General:DataFolder` unless it is an absolute path.
For the default `DataFolder` value, put `sales-analysis.json` in
`PivotController/DataSource`. JSON data must be an array of objects. Index names
must be unique (case-insensitive), 1–64 characters, and contain only letters,
digits, `.`, `_`, or `-`.

| Option | What it does |
|---|---|
| `DefaultIndexId` | Selects the index when the client does not send an index name. It must match an enabled index. |
| `Indexes` | Lists the configured data sources. |
| `Index` | Stable identifier used by requests and index management APIs. |
| `Name` | Human-readable index description. |
| `SourceType` | `Json`, `Csv`, `PostgreSQL`, or `Sqlite`. |
| `SourcePath` | File path for JSON/CSV indexes. Required for those source types. |
| `Enabled` | Enables or disables the index. Disabled indexes cannot serve pivot requests. |
| `AutoReload` | Reloads the source on a schedule when `true`. |
| `ReloadIntervalMinutes` | Reload interval; must be greater than zero when auto-reload is enabled. |
| `MappingEnabled` / `FieldMappings` | Optionally sets source field types explicitly; see the example below. |

**What changes in the client?** With the sample URL above, no client index
parameter is required: `Sales-Json` is selected by default. The fields available
in the client Field List come from that source. If you change the selected
source, update any `rows`, `columns`, or `values` field names to fields in the
new source. The client may explicitly override the default with `?index=OtherIndex`,
but the included sample intentionally leaves the index selection to the server.

#### CSV source options

For CSV, set `SourceType` to `Csv` and set the parsing options to match the file:

```json
{
  "Index": "Products-Csv",
  "Name": "Product data",
  "SourceType": "Csv",
  "SourcePath": "products.csv",
  "CsvDelimiter": ";",
  "CsvDecimalSeparator": ",",
  "CsvThousandsSeparator": ".",
  "Enabled": false,
  "AutoReload": false,
  "ReloadIntervalMinutes": 0,
  "MappingEnabled": false,
  "FieldMappings": []
}
```

`CsvDelimiter` is a column separator such as `,`, `;`, or `|`.
`CsvDecimalSeparator` accepts `Dot`/`.` or `Comma`/`,`; `CsvThousandsSeparator`
accepts `Comma`/`,`, `Dot`/`.`, or `Space`/` `. Enable the index before selecting
it as the default. CSV files need a header row.

#### Database source options

The included database providers are PostgreSQL and SQLite. A database index
requires a database type, connection, and one read-only `SELECT` or `WITH`
query. The connection string may be written inline or referenced by name; the
inline value takes precedence.

```json
"ConnectionStrings": {
  "SalesDatabase": "Host=localhost;Port=5432;Database=sales;Username=YOUR_USER;Password=YOUR_PASSWORD"
},
"DataServer": {
  "DefaultIndexId": "Sales-Database",
  "Indexes": [
    {
      "Index": "Sales-Database",
      "Name": "Sales database",
      "SourceType": "PostgreSQL",
      "DatabaseType": "PostgreSQL",
      "ConnectionStringName": "SalesDatabase",
      "Query": "SELECT * FROM salesdata",
      "Enabled": true
    }
  ]
}
```

Use the corresponding SQLite connection string and `DatabaseType` for SQLite.
SQL Server, MySQL/MariaDB, and Oracle require provider packages that are not
included in this project. Keep real database credentials in deployment secrets
or environment-specific configuration; do not commit them.

**What changes in the client?** The client still posts to the same pivot
endpoint. It does not connect directly to PostgreSQL or SQLite. Use the field
names returned by the query when defining the Pivot Table layout.

#### Field mappings

Mapping is optional. Enable it when inferred field types do not match the data:

```json
"MappingEnabled": true,
"FieldMappings": [
  { "FieldName": "Price", "Type": "Number" },
  { "FieldName": "OrderDate", "Type": "Date" },
  { "FieldName": "ProductName", "Type": "String" }
]
```

Types are `Number`, `Date`, and `String`; each field name must exist in the
source. The server applies these types while processing client reports, which
affects operations such as aggregation, sorting, and grouping. The client uses
the same field names; it does not need to duplicate the mapping in its
`dataSourceSettings`.

### 2. Configure general server settings

```json
"General": {
  "Port": 8765,
  "MinimumLogLevel": "Info",
  "DataFolder": "DataSource"
}
```

| Option | What it does |
|---|---|
| `Port` | HTTP listener port, from 1 to 65535. Changing it requires a server restart. |
| `MinimumLogLevel` | Server logging threshold: `Info`, `Warn`, `Error`, or `Fatal`. Applies at runtime. |
| `DataFolder` | Root for source files, index definitions, persisted settings, and related server data. Relative paths resolve from the application content root; changing it requires a restart. |

**What changes in the client?** When `Port` changes, update the host and port
in `dataSourceSettings.url`. `DataFolder` and `MinimumLogLevel` are server-side
only; they do not change the Pivot Table configuration. The default sample
client URL corresponds to port `8765`.

### 3. Configure data storage and caching

```json
"DataStorage": {
  "RetryAttempts": 3,
  "CacheSizeLimit": 100,
  "KeepDataOnRefresh": true,
  "MaxCacheItemSizeBytes": 0
}
```

| Option | What it does |
|---|---|
| `RetryAttempts` | Retries transient source-load failures; valid range is 0–30. |
| `CacheSizeLimit` | Maximum cached pivot-engine responses per index. Least-recently-used entries are evicted; `0` disables response caching. |
| `KeepDataOnRefresh` | Keeps the last successfully loaded data available if a refresh fails when `true`; otherwise the index can become unavailable. |
| `MaxCacheItemSizeBytes` | Maximum estimated size of a cached data set. `0` means unlimited; an oversized data set is loaded but not cached. This limit applies when response caching is enabled. |

**What changes in the client?** These options are transparent to the client:
it continues sending requests to the same endpoint. They affect server load,
memory use, and what happens during source refresh failures. Restart the server
after changing cache limits.

### 4. Configure database query timeouts

These settings apply only to database-backed indexes:

```json
"DatabaseQuery": {
  "CommandTimeoutEnabled": true,
  "CommandTimeLimit": 150
}
```

`CommandTimeoutEnabled` determines whether the Data Server applies its
`CommandTimeLimit` or lets the database driver's timeout apply.
`CommandTimeLimit` is measured in seconds; `0` means unlimited when the custom
timeout is enabled, and negative values are invalid.

**What changes in the client?** Nothing in the Pivot Table URL or report
settings changes. A timed-out database request is returned as an API failure;
the client should handle the failed request using its usual error handling.

### 5. Configure security and browser origins

For local development with authorization disabled:

```json
"Security": {
  "Authorization": {
    "Enabled": false,
    "Username": "",
    "Password": ""
  },
  "CORS": {
    "AllowOrigin": "*"
  }
}
```

For a protected deployment, set credentials and specify the exact browser origin:

```json
"Security": {
  "Authorization": {
    "Enabled": true,
    "Username": "YOUR_SERVER_USER",
    "Password": "SET_A_SECRET"
  },
  "CORS": {
    "AllowOrigin": "https://pivot.example.com"
  }
}
```

When authorization is enabled, both credentials are required and `AllowOrigin`
must contain one or more exact `http://` or `https://` origins, comma-separated.
Do not use `*` with authorization enabled. An origin contains the scheme, host,
and optional port, but not a path (for example `http://localhost:5500`).
Authorization protects `/api/*`; CORS controls which browser origins may read
the responses. CORS is enforced by browsers, not command-line clients.

**What changes in the client?** For cross-origin browser use, set
`AllowOrigin` to the page's exact origin. If authorization is enabled, requests
must also carry valid Basic Authentication credentials. The current public
sample does not include production credential handling; do not place production
secrets in browser JavaScript. Prefer a trusted same-origin backend/proxy or an
appropriate secured client integration.

### 6. Configure HTTPS

HTTPS is optional. When enabled, configure exactly one certificate source:

```json
"Https": {
  "Enabled": true,
  "PfxPath": "certificates/server.pfx",
  "PfxPassword": "SET_A_SECRET",
  "PemCertPath": null,
  "PemKeyPath": null,
  "StoreLocation": "CurrentUser",
  "StoreName": "My",
  "Thumbprint": null,
  "SubjectName": null,
  "Protocols": "HTTP1_AND_HTTP2"
}
```

Choose one of these certificate approaches:

| Approach | Settings |
|---|---|
| PFX | Set `PfxPath` and, if required, `PfxPassword`. |
| PEM | Set both `PemCertPath` and `PemKeyPath`; a full-chain certificate file is supported. |
| Certificate store | Set `Thumbprint` or `SubjectName`, and configure `StoreLocation` (`CurrentUser` or `LocalMachine`) and optionally `StoreName` (commonly `My`). |

`Protocols` accepts `HTTP1`, `HTTP2`, or `HTTP1_AND_HTTP2`. The certificate
must be available to the server and include its private key. Restart after
changing HTTPS settings. Keep certificate passwords in a startup secret source;
the settings API does not persist or return the PFX password.

**What changes in the client?** Change the endpoint scheme to `https://` and use
the HTTPS listener's port. The browser page should also be served over HTTPS;
the configured certificate must be trusted by the browser. For cross-origin
requests, the CORS origin must use the matching HTTPS scheme.

### 7. Configure report sharing

Report sharing is optional. It can expose report endpoints and save the client
report configuration sent with pivot requests:

```json
"ReportSharingOptions": {
  "EndpointEnabled": false,
  "PathToFolder": "DataServer/reports",
  "ReportExpirationTime": 0,
  "MaxSizeOfFolder": 0,
  "AutoDeletingEnabled": false
}
```

| Option | What it does |
|---|---|
| `EndpointEnabled` | Enables report-sharing endpoints and automatic report saving from pivot requests. When disabled, report operations return 404. |
| `PathToFolder` | Report storage folder. Relative paths resolve under `General:DataFolder`; absolute paths are allowed. |
| `ReportExpirationTime` | Retention in minutes; `0` means reports do not expire. |
| `MaxSizeOfFolder` | Storage quota in MB; `0` means unlimited. |
| `AutoDeletingEnabled` | Allows eviction of the oldest reports to make room when the quota is reached. It does not control expiration cleanup. |

**What changes in the client?** The client continues using
`/api/pivot/post`; it does not have to call the report API for automatic saving.
When enabled, the server saves the report configuration from pivot requests.
Applications that need to list, retrieve, or delete shared reports can use the
`/api/reportsharing/reports` endpoints.

### 8. Configure ASP.NET Core host logging

`Logging` and `AllowedHosts` are ASP.NET Core host settings, separate from the
Data Server's `General` section:

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning"
  }
},
"AllowedHosts": "*"
```

The application uses its own console logger, whose threshold is controlled by
`General:MinimumLogLevel`; `Logging:LogLevel` is retained as standard
ASP.NET Core host configuration but is not the Data Server's primary logging
control. `AllowedHosts` filters accepted host headers; replace `*` with the host
names intended for a deployment.

**What changes in the client?** No Pivot Table setting changes. If host filtering
is restricted, make sure the API URL uses one of the allowed host names.

## Configuration persistence and APIs

Runtime configuration endpoints are available under `/api/config`:

| Endpoint | Section |
|---|---|
| `GET/PUT /api/config/general` | General settings |
| `GET/PUT /api/config/datastorage` | Data storage settings |
| `GET/PUT /api/config/databasequery` | Database query timeout |
| `GET/PUT /api/config/security` | Security settings |
| `GET/PUT /api/config/https` | HTTPS settings |
| `GET/PUT /api/config/reportsharing` | Report sharing |

These endpoints support partial updates. Indexes are managed under
`/api/indexes`, including create/edit, reload, enable/disable, and field-mapping
operations. Check `GET /api/indexes` and its status responses to confirm an
index loaded successfully.

Runtime updates may be persisted under `General:DataFolder` and can take
precedence over values in `appsettings.json` after restart. Index definitions
created or changed through the API are persisted in `indexes.json`. If a value
does not match `appsettings.json`, inspect the persisted settings in the data
folder. Security credentials and the HTTPS PFX password are not persisted or
returned by their settings APIs.

## Troubleshooting

| Symptom | Check |
|---|---|
| Pivot request returns 404 for an index | Confirm the `index` name matches an entry in `DataServer:Indexes`, or correct `DefaultIndexId`. |
| Pivot request returns 423 | The selected index is disabled; set `Enabled` to `true` or enable it through the index API. |
| Pivot request returns 502 or index status is `Failed` | Check the source path, database connection, query, and server logs; confirm the index has loaded. |
| Browser reports a CORS error | Set `Security:CORS:AllowOrigin` to the page origin, including scheme and port but no path. |
| Client cannot reach the server | Confirm the server is running, the port in `General:Port` matches the client URL, and HTTPS is used if enabled. |
| Fields are missing or have unexpected types | Check source column names and configure `MappingEnabled` / `FieldMappings` where necessary. |
| Settings appear to revert after restart | Check the persisted configuration files under `General:DataFolder`, which may override `appsettings.json`. |
