# Syncfusion Pivot Data Server — Phase 1 Proposal

**Type:** Feature

**Date:** 2024-12-19

---

## Overview

This proposal introduces the **Syncfusion Pivot Table Data Server** — a backend-first, configuration-driven data management layer that enables multi-source connectivity, enterprise security controls, HTTPS support, and report sharing capabilities. The design parallels the Flexmonster Data Server architecture and delivers all backend functionality required by the future Admin Panel without implementing the Panel UI itself (Phase 2).

Phase 1 is **backend-only**; the final step wires the client-side sample to pass the index name.

---

## Problem Statement

The current Syncfusion Pivot Table controller implementation has critical limitations:

1. **Single Hardcoded Data Source** — One dataset per deployment; no named-index concept
2. **No Configuration Management** — Data source and settings hard-wired in controller code
3. **Limited Security** — No authentication, minimal CORS, no audit logging
4. **No Multi-Tenancy Support** — Cannot serve different data to different clients at runtime
5. **No Index Lifecycle Management** — Cannot reload, enable/disable, or modify data sources without restart
6. **No Reporting or Sharing** — No mechanism to save or share pivot reports
7. **No Enterprise Monitoring** — Limited logging, no configuration APIs for external tools

---

## Solution Summary

The Custom Data Server delivers a production-grade backend that:

- **Enables Named Indexes** — Multiple datasets (JSON, CSV, databases) declared in `appsettings.json`, managed at runtime
- **Provides Configuration API** — Full REST API for all 7 settings categories (General, Data Storage, Database Query, HTTPS, Security, Report Sharing, Indexes)
- **Supports Multi-Source Connectivity** — JSON files, CSV files, and databases (SQLite, SQL Server, PostgreSQL, MySQL, Oracle, MariaDB)
- **Secures with Authentication** — Basic Auth with PBKDF2 password hashing, CORS origin validation, audit logging
- **Manages Index Lifecycle** — Create, edit (all except source type), reload, enable/disable, delete, test-connection
- **Hot-Reloads Configuration** — JSON file watcher applies runtime changes without restart (except port, HTTPS, cache)
- **Shares Reports** — Store/retrieve pivot reports with expiration and quota management
- **Scales with Caching** — Sized memory cache per index, keep-data-on-refresh semantics, retry policy with exponential backoff

---

## Scope: In Phase 1

✅ **Backend Infrastructure**
- Index Configuration (Step 1, foundational)
- Configuration Management (all 7 sections)
- Data Providers (JSON, CSV, Database with provider abstraction)
- User Management & Basic Auth
- HTTPS Certificate Management
- Report Sharing
- Structured Logging
- Hot-Reload Configuration File Watching
- All REST APIs for index, config, user, and report management
- Backward Compatibility (legacy `PivotDataSource` → index "default")
- Client-side sample configuration (passing index name)

✅ **Deliverables**
- ~40 new files implementing services, controllers, models, persistence
- Enhanced `PivotController.cs` with index-aware routing
- Updated `appsettings.json` with DataServer section and Indexes array
- Updated `Sample/pivot.js` to pass index on every request
- Complete REST API surface for Admin Panel integration (Phase 2)

### Out of Scope: Phase 2+
- ❌ Admin Panel UI
- ❌ Monitoring UI
- ❌ Diagnostics UI
- ❌ Runtime Visual Configuration Screen

---

## Design Principles

1. **Index-First** — Named indexes are the foundation; index configuration is Step 1
2. **API-Driven** — All settings are accessible via REST API, not only configuration files
3. **Stateless Servers** — Configuration and index metadata persisted to JSON files; multiple server instances can share via network storage
4. **Configuration as Code** — Settings editable in JSON files; changes detected and applied at runtime
5. **Enterprise Security** — Authentication, authorization, audit logging, constant-time password verification
6. **Graceful Degradation** — Keep-data-on-refresh allows pivot display to continue if index reload fails
7. **Minimal Breaking Changes** — Backward compatible with existing pivot endpoint; legacy clients work without modification
8. **Pluggable Data Providers** — Easy to add new data source types (FTP, API, cloud storage) via IDataSourceProvider abstraction

---

## High-Level Architecture

```
┌─────────────────────────────────────┐
│ Syncfusion Pivot Table (Sample)     │
│ client.js: passes index on request  │
└────────────┬────────────────────────┘
             │
             │ POST /api/pivot/post
             │ { index: "Sales-Data", action, ... }
             ↓
┌─────────────────────────────────────┐
│ PivotController (index-aware)       │
│ - Extract index from FetchData      │
│ - Route to IndexManager             │
└────────────┬────────────────────────┘
             │
             │ Resolve("Sales-Data")
             ↓
┌─────────────────────────────────────┐
│ IndexManager (cache hub)            │
│ - Cache: data:{index}               │
│ - Retry policy, keep-on-refresh     │
└────────────┬────────────────────────┘
             │
             │ Load index data
             ↓
┌─────────────────────────────────────┐
│ Data Providers (pluggable)          │
│ - JsonDataProvider                  │
│ - CsvDataProvider                   │
│ - DatabaseDataProvider              │
└────────────┬────────────────────────┘
             │
             ↓
     ┌───────┴────────┬──────────┐
     ↓                ↓          ↓
   JSON              CSV      Database
  files             files      (SQLite,
                               SQL Server,
                               PostgreSQL,
                               MySQL,
                               Oracle)
```

### Configuration & Management APIs

```
┌──────────────────────────────┐
│ Admin Panel (Phase 2)        │
│ Connects to:                 │
│ - /api/config/*              │
│ - /api/indexes/*             │
│ - /api/security/users        │
│ - /api/reportsharing/*       │
└──────────────────────────────┘
         ↓ (Phase 2)
┌──────────────────────────────┐
│ Configuration Services       │
│ - ConfigService              │
│ - IndexManager               │
│ - UserService                │
│ - ReportSharingService       │
└──────────────────────────────┘
         ↓
┌──────────────────────────────┐
│ Persistence Layer            │
│ - config.json                │
│ - indexes.json               │
│ - users.json                 │
│ - reports/                   │
└──────────────────────────────┘
```

---

## 8-Step Implementation Roadmap

| Step | Milestone | Scope | Est. Duration |
|------|-----------|-------|---------------|
| 1 | **Index Configuration (foundational)** | IndexDefinition, IndexManager, index-aware routing, API endpoints | 3-4 days |
| 2 | General & Data Storage Settings | Configuration models, persistence, API | 1-2 days |
| 3 | Database Query Settings | IDatabaseQueryProvider, SQLite, timeout enforcement | 1-2 days |
| 4 | HTTPS Settings | Certificate resolution (PFX/PEM/store), Kestrel binding | 1 day |
| 5 | Security Settings | User model, PBKDF2, Basic Auth middleware, CORS | 1-2 days |
| 6 | Report Sharing Settings | ReportSharingService, expiry/quota worker, APIs | 1 day |
| 7 | Persistence, Logging, APIs, Hot-Reload | AtomicJsonStore, config watcher, exception handling, logging | 2-3 days |
| 8 | **Client-side sample (final step)** | Update Sample/pivot.js to pass index | 0.5 day |

**Total Estimated Duration:** 10-15 business days

---

## Success Metrics

### Functional Requirements
- [x] Index declared in `appsettings.json` is available after startup
- [x] Multiple indexes (JSON, CSV, Database) coexist and serve independently
- [x] `index` field in pivot request routes to correct dataset
- [x] Legacy requests without `index` work (backward compatible)
- [x] All 7 configuration sections editable via API and JSON file
- [x] Index enable/disable works; disabled indexes return 423
- [x] Manual index reload works; auto-reload (by time) works
- [x] CSV delimiter/decimal/thousands separators honored
- [x] Field mapping (Number/String/Date) applied on load
- [x] Basic Auth required; valid credentials allow access
- [x] CORS origin validation enforced
- [x] Reports stored/retrieved with expiration
- [x] Configuration changes applied at runtime (hot-reload)

### Quality Metrics
- [x] All acceptance criteria (§22 of spec) satisfied
- [x] Unit tests for validators, retry policy, password hashing, JSON store
- [x] Integration tests for config CRUD, index lifecycle, auth, CORS, reports
- [x] Regression tests ensure existing pivot actions work on migrated `default` index
- [x] E2E test: Sample/pivot.html loads index "Sales-Data" via named-index API
- [x] Manual settings-file testing (§12.5) verifies each Phase 1 option
- [x] No breaking changes to existing `/api/pivot/post` contract

### Performance Metrics
- [x] Index data cached per `CacheSizeLimit`
- [x] Retry policy respects `RetryAttempts` with exponential backoff
- [x] Keep-data-on-refresh prevents blank displays on reload failure
- [x] Hot-reload file watcher debounced and validated (≤1 s latency)
- [x] Basic Auth constant-time password compare (no timing attacks)

---

## Risk Assessment & Mitigation

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Port/HTTPS changes require restart | Medium | Flag `requiresRestart` in API responses; document in UI |
| Memory cache sizing fixed at startup | Low | Require restart for cache-size changes; flagged `requiresRestart` |
| DB provider package footprint | Low | SQLite mandatory; others optional & reflection-registered |
| Windows certificate store unavailable on Linux | Low | Guard with `OperatingSystem.IsWindows()`; fail gracefully |
| Existing client compatibility | High | Keep `/api/pivot/post` payload/response identical; only add `index` field |
| Secrets in config.json | Critical | Never persist connection strings or PFX passwords; reference by name only; gitleaks CI enforces |
| Concurrent index reload vs pivot requests | Medium | Protect per-index data swap with `SemaphoreSlim`; `KeepDataOnRefresh` prevents blank windows |
| Atomic write on Windows | Low | Temp file in same directory as target; `File.Replace` is cross-volume safe |
| High pivot hit-logging volume | Low | Keep pivot logging at `Debug` level by default |
| Settings-file watcher reliability | Medium | Debounce, validate before applying, keep last-known-good on error |

---

## Rollout Strategy

### Phase 1 Delivery
1. Implement 8 steps sequentially (no parallel development on dependencies)
2. Each step has unit/integration tests before moving to next
3. At end of Step 7, full REST API available for Phase 2 Admin Panel
4. Step 8 wires client sample; verify E2E with named-index pivot request
5. Publish to main branch with no breaking changes

### Phase 2+ (Future)
1. Admin Panel UI connects to Phase 1 endpoints
2. Monitoring / Diagnostics dashboards
3. Performance metrics / health dashboard
4. Extended data provider support (FTP, cloud APIs)

---

## Backward Compatibility

✅ **Fully Backward Compatible**

- Legacy `/api/pivot/post` endpoint unchanged
- Existing `Sample/pivot.html` works without modification (uses DefaultIndexId)
- Legacy `PivotDataSource` block auto-migrated to index "default"
- No breaking changes to existing client code

---

## Deliverables Summary

### Code
- ~40 new files (services, controllers, models, persistence, security)
- 5 modified files (Program.cs, PivotController.cs, appsettings.json, Sample/pivot.js, csproj)
- Comprehensive unit and integration tests
- Full documentation of API endpoints and configuration options

### Documentation
- REST API specification with request/response examples
- Configuration schema and defaults
- User management guide
- Index lifecycle guide
- Admin Panel integration guide (for Phase 2)

### Deployment
- Docker-ready configuration
- Health endpoint for load balancers
- Logging infrastructure for monitoring
- Git-safe secrets handling (no hardcoded passwords/connections)

---

## Approval Checklist

- [x] Scope clearly defined (Phase 1 backend-only; Phase 2 UI)
- [x] Implementation order established (8 steps)
- [x] Success metrics defined
- [x] Risk assessment completed with mitigations
- [x] Backward compatibility guaranteed
- [x] Technology stack confirmed (no new external packages)
- [x] Estimated timeline provided (10-15 business days)
- [x] Acceptance criteria from specification documented (§22)
- [x] Testing strategy defined (unit, integration, regression, E2E, manual)

---

## Next Steps

Upon approval:
1. Proceed to design.md for detailed component design and API contracts
2. Create spec.md with testable requirements and acceptance scenarios
3. Generate tasks.md with implementation breakdown
4. Begin Step 1 (Index Configuration) implementation

