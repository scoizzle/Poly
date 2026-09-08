# Follow-ups — PR 66 re-verify a65aa50d (Razor) — 2026-09-08

- Source review: `docs/agent/reviews/2026-09-08-pr66-a65aa50d-razor.md`
- Prior: `docs/agent/reviews/2026-09-08-pr66-a2241db1-razor-followups.md` (fetched from `origin/review/razor-pr66-a2241db1`)
- Target: PR 66 SHA `a65aa50dce560fd62e4c5e69db3b1c821bf08bd8` vs prior tip `a2241db145ed2df36d4e83e91dfe1427b4297591`
- Mode: re-verify
- Verdict: **ship** — F1 closed; no open ship-gate bugs

## Open bugs (must close before ship)

_None. Prior F1 closed at this tip._

## Suggestions

- [ ] **F2** — Bound or close optional-length empty-skip sibling drift. File: `EffectLoweringPass.cs:410-417` vs `DomainEntityInstance.cs:193-198` / `DomainToCSharpExporter.Notify.cs:413-430`. Do: document assign-only empty-skip in guide, **or** teach Create/`ValidateConstraints` the same `!Required ⇒ skip null/empty` length rule so create and assign agree on `Text length(7,20)` + `""`.

- [ ] **F3** — Force void-export assign constraint sibling. File: `EffectLoweringPass.cs:447-451`. Do: OnEntry/ctor lowering (or export) test where constrained assign fails → `throw new InvalidOperationException(…)` and destination not assigned after the throw in the tree.

- [ ] **F4** — Tighten same-tree export oracle. File: `ConstraintAssignLoweringTests.cs:209-239` (and soft ORs `:33`, `:63`). Do: build export via `DomainToCSharpExporter.LowerActionToMethodBody` (sets `ActionResultType`) and assert Failure + `must be >=` before assign on **both** module and export CS; drop soft `|| Failure` that lets throw-shaped trees pass.

- [ ] **F5** — Unique after constraints e2e. File: `EffectLoweringPass.cs:317-332`. Do: one property with Unique + Range (or Pattern); out-of-range fails before mutate without needing a peer; in-range duplicate hits EnsureUnique Failure and leaves prior value.

## Nits

- [ ] **N1** — Constraint check order parity. File: `EffectLoweringPass.cs:353-438`. Do: match declaration order used by Create/`ValidateConstraints`, or document fixed required→range→length→pattern precedence as intentional.

## Process

- [ ] **P1** — When copying Create-factory constraint IR into the shared Lower tree, diff against `ValidateConstraints` null/`is string` gates in the same PR (recurring class: export-shaped checks lack bag null skips). Prefer a shared helper or a checklist item in phenomenal-review Poly hooks. **Partial progress this tip:** assign pattern now null-guards; Create factory (`Notify.cs:433-446`) still bare IsMatch — file separately if desired.

## Disposition of prior items

| ID | Prior status @ a2241db1 | This tip @ a65aa50d |
|----|-------------------------|---------------------|
| **F1** | open bug (ship gate) | **CLOSED** — `And(NotEqual null, Not IsMatch)` @ `EffectLoweringPass.cs:424-438`; test `PatternAssign_Null_DoesNotThrow_RetainsPriorValue` |
| **F2** | open suggestion | still open |
| **F3** | open suggestion | still open |
| **F4** | open suggestion | still open (line refs updated for inserted test) |
| **F5** | open suggestion | still open |
| **N1** | open nit | still open |
| **P1** | open process | still open; assign pattern half addressed |

## F1 close evidence (re-verify)

- Diff `a2241db1..a65aa50d`: 2 files only (`EffectLoweringPass.cs`, `ConstraintAssignLoweringTests.cs`).
- Export/IR: null guard before IsMatch; CS contains `!= null` / `is not null` + `IsMatch`.
- Runtime: InvokeAction null → Failure pattern message + prior retained; no `ArgumentNullException`.
- Soft-skip vs Failure: true-null soft-skips (ValidateConstraints align); bag null→`""` yields Failure (documented in review).
- CI: Build & test SUCCESS; MERGEABLE; CLEAN.
