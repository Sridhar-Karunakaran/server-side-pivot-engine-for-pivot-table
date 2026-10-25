# SPEC — panel-config-precedence (D1)

**Capability:** `panel-config-precedence` — the backend loads the MAUI Admin Panel's `dataserver-admin-config.json` as the **primary** Data Server configuration when hosting in-process, ignoring backend `appsettings.json` data-server sections while that file exists.

**Date:** 2026-10-23

---

## ADDED Requirements

### Requirement: 1. Panel-Mode Bootstrap Entry Point

The backend **SHALL** expose a host-construction entry point (`BuildDataServerHost(panelConfigPath)`) that builds the ASP.NET Core `WebApplication` with the panel file added as the **first** JSON configuration source, ahead of `appsettings.json`, `appsettings.{env}.json`, environment variables, and command line.

#### Scenario: Panel file wins over appsettings

- Given: panel file `General.Port = 5285`; backend `appsettings.json` contains `General.Port = 5000`
- When: The host is built in panel mode and started
- Then: Kestrel binds port **5285**
- And: The startup log names the panel file as the active source

#### Scenario: All sections honor the panel file

- Given: Panel file defines `DataStorage`, `DatabaseQuery`, `Https`, `Security`, `ReportSharingOptions`, and `DataServer.Indexes[]`
- When: The host starts in panel mode
- Then: Every data-server setting and every index resolves from the panel file
- And: Conflicting values in backend `appsettings.json` data-server sections are ignored

### Requirement: 2. Overlay Suppression in Panel Mode

In panel mode, the `{DataFolder}/*.json` persisted overlays **SHALL NOT** be loaded (CL-1). In standalone mode (no panel path), today's behavior — appsettings + overlays with non-null-wins precedence — **SHALL** remain unchanged.

#### Scenario: Standalone mode unchanged

- Given: The backend runs standalone (`dotnet run`, no panel path)
- When: It starts with `{DataFolder}/https.json` present from a prior PUT
- Then: The overlay applies exactly as today (non-null wins, loud log)
- And: No panel-file logic interferes

### Requirement: 3. Missing/Invalid Panel File

A missing, unreadable, or invalid panel file in panel mode **SHALL** fail host construction with a clear error naming the file path; the backend **MUST NOT** silently fall back to `appsettings.json` data-server sections in panel mode.

#### Scenario: Missing file fails loudly

- Given: `panelConfigPath` points to a non-existent file
- When: `BuildDataServerHost` is called
- Then: Construction fails with an error naming the path and stating the file is required in panel mode
- And: No listener is bound

### Requirement: 4. Validation & Reuse

The panel file **SHALL** pass through the existing startup validation pipeline (per-section validators, cross-field rules, path-traversal checks, zero-user rule per `security-user-store`) before `Listen`. A validation failure fails startup exactly as today's invalid `appsettings.json` would.

#### Scenario: Invalid imported-style value fails fast

- Given: Panel file with `General.Port: 99999`
- When: The host builds in panel mode
- Then: Startup fails with the existing port-range message
- And: Exit is non-zero / error surfaces to the panel's start-failure dialog

### Requirement: 5. Restart Re-Read

Every host Start/Restart in panel mode **SHALL** re-read the panel file from disk (no caching across host builds), so panel write-throughs and imports take effect on the next Start/Restart.

#### Scenario: External edit honored on restart

- Given: A running panel-mode host
- When: The panel file's Port changes on disk and the host restarts
- Then: The new host binds the new port

### Requirement: 6. Source Logging

Startup **SHALL** log which source produced the active configuration — `"Data Server configuration loaded from <panel path> (panel mode)"` vs `"... from appsettings.json (standalone mode)"` — and the deprecation note for ignored `appsettings.json` data-server sections in panel mode.

#### Scenario: Log identifies mode

- Given: Panel mode start
- When: Startup completes
- Then: The log line names the panel file path and "(panel mode)"