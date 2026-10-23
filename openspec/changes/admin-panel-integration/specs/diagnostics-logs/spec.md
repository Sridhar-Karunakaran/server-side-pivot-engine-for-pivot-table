# SPEC — diagnostics-logs (D5)

**Capability:** `diagnostics-logs` — the `GET /api/diagnostics/logs` endpoint serving a bounded tail of the server's current rolling JSONL log for the MAUI Admin Panel's Diagnostics tab.

**Date:** 2026-10-23

---

## Requirements

### 1. Endpoint Contract

```
GET /api/diagnostics/logs?lines=200      (default 200 — locked (c); hard cap 1000)
→ 200 { "lines": [ { "timestamp", "level", "category", "message" } ],
        "file": "<path of the current rolling JSONL file>" }
Errors: 422 invalid lines parameter (non-numeric / negative / > cap coerced or rejected)
```

Entries **SHALL** preserve file order (oldest → newest of the returned tail).

#### Scenario: Default tail

- Given: A log file with 500 entries
- When: `GET /api/diagnostics/logs` (no query)
- Then: 200 with the **last 200** entries in file order, `file` naming the current JSONL path

#### Scenario: Explicit lines

- Given: The same file
- When: `GET /api/diagnostics/logs?lines=50`
- Then: The last 50 entries are returned

### 2. Source & Filtering

The endpoint **SHALL** read only the **current** rolling file (`{DataFolder}/logs/dataserver-{yyyyMMdd}.jsonl`) — no historical files, no arbitrary path parameters (CL-7 scope). Entries below the active `MinimumLogLevel` **SHALL NOT** be returned (server-side filter, consistent with the console provider's threshold). Categories remain the existing set (`DataServer.Config | Security | Indexing | DatabaseQuery | ReportSharing | Https | Pivot`).

#### Scenario: Level filter applies server-side

- Given: `MinimumLogLevel = Error`
- When: `GET /api/diagnostics/logs`
- Then: Only Error/Fatal entries appear, even if the file contains older Info/Warn lines

#### Scenario: No filters beyond lines

- Given: The Diagnostics tab's v1 scope (c)
- When: Any request
- Then: No category/date/text filters are accepted or applied (only `lines`)

### 3. Secret Redaction

The payload **MUST NOT** contain secrets (connection strings, passwords, tokens). Because the logger already redacts at write time, the endpoint **SHALL** additionally scan rendered lines for credential patterns before response (defense in depth, D6).

#### Scenario: No secrets in the tail

- Given: A log history that includes database index operations
- When: `GET /api/diagnostics/logs`
- Then: No line contains connection-string or password material

### 4. Auth & CORS

The endpoint **SHALL** require Basic Auth when enabled (401 otherwise) and follow the existing CORS rules.

#### Scenario: Auth applies

- Given: Auth enabled
- When: `GET /api/diagnostics/logs` without credentials
- Then: 401; with a valid credential, 200

### 5. Empty & Edge States

- Empty/missing log file ⇒ 200 with `lines: []` and `file: null` (or the expected path with a note) — never an error.
- Rolled-midnight files: the endpoint reads the file current at request time; no cross-file stitching.

#### Scenario: Fresh server

- Given: A just-started server with an empty log file
- When: `GET /api/diagnostics/logs`
- Then: 200 with an empty `lines` array and the file path
- And: The panel renders its empty-state hint