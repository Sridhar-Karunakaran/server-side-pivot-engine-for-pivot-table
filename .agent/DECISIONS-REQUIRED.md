# DECISIONS-REQUIRED — parking lot

Lane-3 decisions that block implementation until a human answers. An agent
may propose, never decide. Empty is valid; the file must exist either way
(`policies/autonomy-boundary.md`).

## Open

(none — all decisions answered as of 2026-10-23)

## Answered

- **DR-1 (D6 / credential rotation) — ANSWERED (user, 2026-10-23): use the recommended
  default.** Sanitize `PivotController/appsettings.json` (placeholder + user-secrets/env
  path documented in README) as a normal implementation task; the owner accepts that
  repo history still carries the old test-only credential and will rotate it when this
  test credential is reused anywhere real. T-003 unblocked → `todo`.
- **DR-2 (D3 / first admin user) — ANSWERED (user, 2026-10-23): use the recommended
  default.** No migration seed: the first admin is created via the panel's Security tab
  (Add User view); enabling Basic Auth with zero users is rejected (spec §3, Q7/CL-4).
  T-006 unblocked → `todo`.
- **DR-3 (D5 / log file location) — ANSWERED (user, 2026-10-23): use the recommended
  default.** `logs/dataserver-{yyyyMMdd}.jsonl` **relative to the content root** (the
  Phase-1 design.md §13 wording), NOT inside `{DataFolder}`. The endpoint reads the
  current file only; no path traversal. T-008 unblocked → `todo`.
- **DR-4 (D4 / monitoring retention) — ANSWERED (user, 2026-10-23): use the recommended
  default.** Registry = in-flight entries + completed entries retained 60 seconds
  after completion (for "recent activity"), then dropped. No bodies/credentials kept.
  T-007 unblocked → `todo`.
- **DR-5 (harness tiers) — ANSWERED (user, 2026-10-23): use the recommended default.**
  L0 floors stay until the first seeded task establishes the build gate green; raise
  to L1 (test project + coverage-diff ≥ 0.80) with the first test-bearing task and
  regenerate the lock at that point. Never lower.
- **Companion-repo DR-A (Syncfusion license key) — ANSWERED (user, 2026-10-23): do NOT
  register a license key now.** The syncfusion-license gate is REMOVED from the MAUI
  contract (it would fail forever until a key exists); a `license-key` feature task is
  seeded instead (config/env wiring only, key provided later by the owner). T-001 unblocked.
- **Companion-repo DR-B (platform targets) — ANSWERED (user, 2026-10-23): Windows,
  macOS, iOS AND Android** — all four declared in the multi-target; **Windows is the
  priority**. Platform-build evidence (outer lane) runs on Windows first; other
  platform issues are resolved later, per the standing Q2 policy.
- In-process hosting vs child process — **in-process** (user, round 2). Encoded in
  companion repo specs; D1 provides the bootstrap entry point.
- Remote/standalone panel connection — **future scope** (user). Nothing in D1–D6 builds it.