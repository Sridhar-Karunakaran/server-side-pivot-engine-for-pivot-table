# SPEC — monitoring (D4)

**Capability:** `monitoring` — the `GET /api/monitoring` endpoint exposing server process memory and active-connection data for the MAUI Admin Panel's Monitoring tab.

**Date:** 2026-10-23

---

## Requirements

### 1. Endpoint Contract

```
GET /api/monitoring
→ 200 {
     "workingSetBytes": <long>,     // server process working set — metric locked in (b)
     "uptime": "<duration since host start>",
     "activeConnections": [
       { "requestType": "<method + path>",
         "initiationTimestamp": "<ISO-8601>",
         "requestDuration": "<elapsed of in-flight entry>",
         "ipAddress": "<remote IP>",
         "index": "<index name | null>" }
     ]
   }
```

#### Scenario: Shape and units

- Given: A running server with one in-flight pivot request for index `Sales-Data`
- When: `GET /api/monitoring`
- Then: 200 with a positive `workingSetBytes`, a non-empty `uptime`, and one entry whose `requestType`, `initiationTimestamp`, `requestDuration`, `ipAddress`, and `index` are populated

### 2. Active Request Registry

The server **SHALL** maintain an in-memory registry populated by middleware at request start (request id, method+path, timestamp, remote IP) and cleared at request completion. `index` **SHALL** be resolved where cheaply available (pivot `?index=`/body index; index endpoints' `{index}` route value); otherwise `null`. The registry **MUST NOT** persist request bodies, headers, credentials, or query strings (D6).

#### Scenario: Completed requests disappear

- Given: A finished pivot request
- When: `GET /api/monitoring`
- Then: The entry is gone (only in-flight entries are listed)

#### Scenario: Index attribution where available

- Given: An in-flight `POST /api/pivot/post?index=Sales-Data`
- When: `GET /api/monitoring`
- Then: The entry shows `index: "Sales-Data"`

### 3. Auth & CORS

The endpoint **SHALL** require Basic Auth when enabled (401 otherwise) and follow the existing CORS rules. The payload **MUST NOT** contain secrets of any kind (D6).

#### Scenario: Monitoring respects auth

- Given: Auth enabled
- When: `GET /api/monitoring` without credentials
- Then: 401; with a valid user credential, 200

### 4. Performance

The registry **SHALL** be O(1) add/remove per request with no I/O on the hot path; `GET /api/monitoring` **SHALL** itself appear in the registry (self-observability) or be excluded consistently — the panel renders whatever the server reports without client-side filtering.

#### Scenario: Low overhead

- Given: Load testing with concurrent requests
- When: The registry is active
- Then: Throughput/latency overhead stays negligible (no serialization of bodies, no locks held across awaits)

### 5. Server Stopped Behavior

When the host is stopped (panel mode), the endpoint is unreachable by definition (no listener); the panel handles that state with its stopped/unreachable dialog — no server-side requirement.

#### Scenario: Unreachable host

- Given: Host stopped
- When: The panel probes `/api/monitoring`
- Then: Connection failure → panel's stopped/unreachable dialog (panel-side behavior)