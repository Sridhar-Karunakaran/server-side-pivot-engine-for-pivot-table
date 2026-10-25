# SPEC — credential-hygiene (D6 — normative)

**Capability:** `credential-hygiene` — normative security requirements governing secrets across the Data Server: no plaintext credentials in git-tracked files, never logged, never returned by any API.

**Date:** 2026-10-23

---

## ADDED Requirements

### Requirement: 1. No Plaintext Secrets in Version Control

The repository **SHALL NOT** contain plaintext credentials in git-tracked files. Specifically:

- The shipped `PivotController/appsettings.json` **MUST NOT** contain `Security.Authorization.Username/Password` values (the current plaintext pair used for testing is removed by this change's template sanitization).
- Test PKI (`Cert/PEM-Cert/`) may remain (public test CA/leaf material, already used by `verify-https.sh`).
- Standalone testing credentials **MUST** come from user-secrets or environment variables, documented in `README.md`.

#### Scenario: Template is clean

- Given: A fresh clone of this repository
- When: `PivotController/appsettings.json` is inspected
- Then: No username/password credential values are present
- And: A README section documents the user-secrets/env testing path

#### Scenario: History normalization

- Given: The repository history contains the old plaintext pair
- When: This change lands
- Then: HEAD no longer carries the secret; the README instructs operators to rotate any credential previously committed (documented limitation: history rewrite is out of scope)

### Requirement: 2. Never Logged

No secret value — connection strings (full or partial), passwords, PFX passwords, Basic credentials, API keys — **SHALL** be written to any log (console, rolling JSONL file) by any code path. The existing redaction audit **SHALL** be extended to cover the new endpoints (`config-import`, `security-user-store`, `monitoring`, `diagnostics-logs`) and the panel-mode bootstrap.

#### Scenario: Import does not log secrets

- Given: An import file containing a PFX password
- When: Import is applied
- Then: No log line (any level, any category) contains the password or connection strings

### Requirement: 3. Never Returned

No API response **SHALL** include secret material:

- `GET /api/config/https` omits `PfxPassword` (existing sanitized behavior — preserved).
- User APIs return no hash/salt (see `security-user-store`).
- `GET /api/monitoring` and `GET /api/diagnostics/logs` payloads contain no credentials (see their specs).
- `POST /api/config/import` responses echo no secret values.

#### Scenario: Config GETs stay sanitized

- Given: HTTPS configured with a PFX password
- When: `GET /api/config/https`
- Then: The response contains no password field value (null/omitted as today)

### Requirement: 4. Storage Rules

- Secrets at rest: only where the existing design already stores them — `users.json` **hashes** (never plaintext), sanitized `https.json` (no password), user-secrets/env for standalone testing. The panel file (`dataserver-admin-config.json`, panel repo) never stores passwords (its spec enforces the same rule).
- `Https.PfxPassword` survives restarts only via the documented appsettings/user-secrets path in standalone mode (existing behavior); panel mode holds it in memory after import/PUT.

#### Scenario: User store never plaintext

- Given: Any user created/updated
- When: `DataServer/users.json` is inspected
- Then: Only PBKDF2 hash + salt are present

### Requirement: 5. Verification Gate

A credential-hygiene verification step **SHALL** be part of the implementation gate for this change (and the companion panel change): scan templates + a representative log run for credential patterns (passwords, `Password=`, connection-string fragments) and assert zero findings.

#### Scenario: Gate runs in CI

- Given: The change's verification suite
- When: The hygiene scan executes against templates and generated logs
- Then: Zero credential-pattern findings; the gate fails otherwise