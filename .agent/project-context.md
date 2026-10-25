# PROJECT CONTEXT — Syncfusion Pivot Data Server (backend)

**GENERATED: Stack detection** (verify with `python3 .agent/tools/harness.py doctor`)

| Layer | Detected | Evidence |
|---|---|---|
| Framework | ASP.NET Core `net10.0` | `PivotController/PivotController.csproj` → `Microsoft.NET.Sdk.Web`, `<TargetFramework>net10.0</TargetFramework>` |
| Packages | Syncfusion.Pivot.Engine 35.1.41 · Npgsql 8.0.7 · Microsoft.Data.Sqlite 10.0.* · Newtonsoft.Json 13.0.4 · Swashbuckle 6.4.0 · Microsoft.AspNetCore.OpenApi 7.0.0 | csproj `<PackageReference>` list |
| UI | none (API server) | no Razor/MAUI/Blazor assets |
| Tests | none on arrival | no `*.Tests.csproj`, no test runner packages → tier L0 |
| Oracles | verify-https.sh (33 checks) · verify-cache.sh | `PivotController/*.sh`, referenced from Phase-1 spec deltas |

**GENERATED: Architecture map** (from `dotnet list PivotController/PivotController.csproj reference` + file tree; no project references outside the solution):

```
Program.cs ── Kestrel bootstrap (port/TLS from General/Https sections)
  ├── Controllers/PivotController.cs ──── /api/pivot/post (index-aware routing)
  ├── Indexing/IndexManager.cs ───────── cache hub: data:{index}, engine:{index}:{hash}
  │     └── IndexReloadWorker.cs ─────── PeriodicTimer auto-reload per index
  ├── Configuration/Controllers ──────── /api/config/{general,datastorage,databasequery,https,security,reportsharing}
  ├── Configuration/Models ────────────  settings models + validators
  ├── Configuration/Services ───────────  persistence ({DataFolder}/*.json), watcher, merge-patch
  ├── Core/Persistence ────────────────  AtomicJsonStore
  ├── Core/Security ────────────────────  PBKDF2 hasher, constant-time compare, OriginValidator
  ├── Security/PivotBasicAuthMiddleware.cs  Basic auth (Security:Authorization) + CORS
  ├── Reports/{Controllers,Models,Services} report sharing + sweep worker
  └── Data/ + DataSource/ ──────────────  Json/Csv/Database providers (SQLite, PostgreSQL)
```

**Hotspots** (`git log` churn): `Program.cs`, `PivotController.cs`, `appsettings.json`,
`Indexing/IndexManager.cs`, `Configuration/*` — weight review findings there.

**Boundaries**

- One server process; the MAUI Admin Panel (companion repo) embeds it **in-process** and
  is the sole launcher in panel deployments. Panel file `dataserver-admin-config.json`
  becomes the primary config source (D1). `{DataFolder}/*.json` overlays are
  **standalone-mode only**.
- REST surface frozen (Phase 1); D1–D6 are additive.
- `Security.Authorization` (appsettings) is **deprecated** by D3 (multi-user store).
- Secrets: never in logs, never in API responses, never plaintext in tracked files.

**Conventions**

- C# nullable enabled, implicit usings; Newtonsoft.Json for config (matching existing
  AtomicJsonStore), System.Text.Json not introduced by this program.
- Errors: RFC 7807 problem details via the existing exception filter; 422 validation,
  404 unknown, 409 duplicate, 502 upstream failure, 504 timeout — mirror existing handlers.
- OpenSpec `bugfix-feature` schema; every change ships EXPLORATION/proposal/design/
  tasks + delta specs before code (`openspec/config.yaml`).

**Verification approach**

- Inner: build + openspec-validate + no-secrets (+ advisory license placement).
- Outer: live oracles `verify-https.sh`, `verify-cache.sh` (and, once extracted,
  `verify-dbtimeout.sh`) against a locally running server + test PKI in `PivotController/Cert/`.
- D3/D4/D5 endpoints get their own characterization scripts as they land (seeded tasks),
  then join the outer lane.

**Known debt (hand-written)**

- `verify-dbtimeout.sh` not yet extracted; Phase-1 timeout verification was manual/sandboxed.
- No test project; tier L0 → first tasks establish characterization.
- `appsettings.json` still ships the Phase-1 test credential; D6 template sanitization
  is the first D6 task and MUST NOT be pushed before the user rotates it.
- Sample/pivot.js client-side integration (custom-data-server step 8) still open.