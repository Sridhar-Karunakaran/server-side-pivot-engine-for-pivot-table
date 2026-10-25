# DECISIONS-REQUIRED — parking lot

Lane-3 decisions that block implementation until a human answers. An agent
may propose, never decide. Empty is valid; the file must exist either way
(`policies/autonomy-boundary.md`).

## Open

- **DR-1 (D6 / credential rotation):** the tracked `PivotController/appsettings.json`
  currently contains a live test credential. The sanitization task replaces it with
  a placeholder + user-secrets path, but the REAL credential must be rotated by
  the owner first (history still carries it). *Question: confirm rotation done /
  accept history exposure for this test-only repo?* — blocks the D6 template task.
- **DR-2 (D3 / first admin user):** with `Security.Authorization` deprecated, the
  user store starts empty. Enabling Basic Auth with zero users fails startup by
  design. *Question: seed a first admin via migration (from old pair) or require
  panel-created first user?* Spec default: panel-created only (matches Q7 answer).
- **DR-3 (D5 / log file location):** rolling JSONL logger writes to
  `logs/dataserver-{yyyyMMdd}.jsonl` per design.md §13. *Confirm final path*
  (`{DataFolder}/logs/` vs `logs/` relative to content root) before the endpoint
  task — the spec pins behavior, not the path.
- **DR-4 (D4 / monitoring retention window):* how long an active-connection
  registry entry survives after completion (needed for "active connections" to be
  meaningful to an admin). Spec default: in-flight only + last 60s completed.
- **DR-5 (harness tiers):* current L0 floors are generated defaults. Raise to L1
  (test project + coverage-diff ≥ 0.80) for the D-delta work, yes/no? Recommended
  yes as the first seeded task after characterization.

## Answered

- In-process hosting vs child process — **in-process** (user, round 2). Encoded in
  companion repo specs; D1 provides the bootstrap entry point.
- Remote/standalone panel connection — **future scope** (user, latest round).
  Nothing in D1–D6 builds it.