---
id: autonomy-boundary
kind: policy
version: 1
appliesTo: [all]
---

# Autonomy boundary — Syncfusion Pivot Data Server

Decide, journal, keep going — EXCEPT where a park category matches. Parked
decisions go to `../DECISIONS-REQUIRED.md` and block implementation until a
human answers.

## Park categories (this repo)

Risk globs (`harness.yaml risk.requiresHumanApproval`):

- `**/Security/**` — auth/store changes (D3 touches exactly here)
- `**/appsettings*.json` — config template (D6 sanitization lands here)
- `**/DataServer/*.json` — runtime persistence stores (users/indexes/config)
- `**/*.csproj` — dependency or TFM changes
- `**/Controllers/**` — the frozen REST surface

Program-specific parks (from the open decisions list):

- Credential rotation before template sanitization (DR-1)
- First-admin bootstrap semantics (DR-2)
- Log file location (DR-3)
- Monitoring retention window (DR-4)
- Tier raises above L0 (DR-5)

## The three lanes

1. **Inner (decide alone):** code edits inside existing patterns, docs, tests
   within existing suites, spec wording fixes.
2. **Outer (decide + evidence):** anything with a gate in `harness.yaml`
   inner lane — the gate output IS the approval.
3. **Park (ask):** anything matching a glob above or the D-decisions. Propose
   options with trade-offs, then stop and wait.