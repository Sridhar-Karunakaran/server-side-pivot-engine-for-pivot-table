# SPEC — security-user-store (D3)

**Capability:** `security-user-store` — a server-side multi-user store with CRUD API; Basic Authentication validates against it across **all** Data Server endpoints; the single `Security.Authorization` pair is deprecated.

**Date:** 2026-10-23

---

## Requirements

### 1. User Store

The backend **SHALL** maintain `DataServer/users.json` (atomic writes, same store family as `indexes.json`), holding one record per user: `{ username, hash (PBKDF2), salt, createdAt, updatedAt }`. **No plaintext or reversible password material is ever stored.** Usernames are unique, case-insensitive, and immutable after creation.

#### Scenario: User creation stores only a hash

- Given: `POST /api/security/users { "username": "admin", "password": "..." }`
- When: Created
- Then: `users.json` contains the username, a PBKDF2 hash, and salt — no plaintext
- And: The response is 201 with `{ username, createdAt }` only

### 2. User CRUD API

```
GET    /api/security/users              → 200 [ { username, createdAt, updatedAt } ]
POST   /api/security/users              { username, password }       → 201 { username, createdAt }
PUT    /api/security/users/{username}   { newPassword }              → 200 { username, updatedAt }
DELETE /api/security/users/{username}                                → 204
```

- GET **MUST NOT** return hashes/salts. PUT rotates the credential (old password stops working immediately). DELETE removes the record and the credential stops authenticating immediately. Unknown `{username}` ⇒ 404. Duplicate/invalid username on POST ⇒ 409/422.

#### Scenario: Password rotation

- Given: User `admin` with password P1
- When: `PUT /api/security/users/admin { "newPassword": "P2" }`
- Then: 200; P1 fails with 401 on the next request; P2 succeeds

#### Scenario: Delete removes access instantly

- Given: User `admin`
- When: `DELETE /api/security/users/admin` (204)
- Then: `admin`'s credential returns 401 immediately on any endpoint

### 3. Basic Auth Against the Store

When `Security.Authorization.Enabled` is true, the Basic-auth middleware **SHALL** validate the presented credential against **every** user record (salted hash + constant-time comparison); a match on any user grants access. A miss ⇒ 401 with `WWW-Authenticate: Basic realm`. This applies to **all** Data Server endpoints: `/api/pivot/*`, `/api/indexes/*`, `/api/config/*`, `/api/security/users/*`, `/api/config/import`, `/api/monitoring`, `/api/diagnostics/logs`, `/api/reportsharing/*` — with the existing CORS preflight exemption (OPTIONS never requires credentials).

#### Scenario: Auth spans pivot traffic

- Given: Auth enabled, user `analyst`
- When: `POST /api/pivot/post` without credentials
- Then: 401 + `WWW-Authenticate: Basic realm`
- When: The same request carries `analyst`'s credential
- Then: 200 with pivot data

#### Scenario: Preflight still open

- Given: Auth enabled
- When: A CORS preflight OPTIONS arrives without credentials
- Then: 204 with CORS headers (unchanged §5.2.11 behavior)

### 4. Zero-User Rule (CL-4, server-side)

Enabling authorization (`Security.Authorization.Enabled = true`) while the user store is **empty SHALL** be rejected: `PUT /api/config/security` returns 422 with a message directing to user creation; startup with that state **fails fast** (mirroring §5.2.8's missing-credentials behavior). Deleting the **last** user while auth is enabled **SHALL** be allowed with a 200/204 plus a warning header/log ("last user removed — authentication will reject all requests until a user is added"); the panel surfaces this state via the zero-user guard on next enable.

#### Scenario: Enable with no users rejected

- Given: Empty user store, auth disabled
- When: `PUT /api/config/security { "authorization": { "enabled": true } }`
- Then: 422 with the zero-user message; auth stays disabled

#### Scenario: Last-user deletion warns

- Given: Auth enabled, exactly one user
- When: `DELETE /api/security/users/{thatUser}`
- Then: 204 with a warning that no users remain
- And: Subsequent authenticated requests all fail 401 until a user is created

### 5. Deprecation of the Single Pair (CL-3)

`Security.Authorization.Username/Password` **SHALL** be deprecated: the shipped `appsettings.json` template removes them; authentication resolves **only** against the user store; a stale pair present in a custom config produces a one-time startup deprecation log. **BREAKING** for standalone test setups: they must provision a user (user-secrets/env + `POST /api/security/users`, or the panel's Security tab).

#### Scenario: Legacy pair no longer authenticates

- Given: D3 applied; `appsettings.json` still carries a stale pair
- When: A request presents that pair with no matching user record
- Then: 401 (pair ignored), with the deprecation note logged once at startup

#### Scenario: Startup fail-fast parity

- Given: `Authorization.Enabled: true` and zero users at startup
- When: The application starts
- Then: Startup fails with the zero-user message (same class as §5.2.8)

### 6. Credential Hygiene (D6)

User passwords **MUST NOT** be logged, returned by any API, or persisted in plaintext anywhere. Validation timing **MUST** be constant-time per user. The store file **MUST** be excluded from logs and diagnostics output.

#### Scenario: No password material in any surface

- Given: Any sequence of user operations
- When: Responses, logs, and diagnostics endpoints are inspected
- Then: No password, hash, or salt values appear beyond the store file itself