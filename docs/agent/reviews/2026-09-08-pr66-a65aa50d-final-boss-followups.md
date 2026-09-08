# Follow-ups — PR 66 re-verify a65aa50d (Final Boss) — 2026-09-08

- Source review: `docs/agent/reviews/2026-09-08-pr66-a65aa50d-final-boss.md`
- Prior Razor: `docs/agent/reviews/2026-09-08-pr66-a65aa50d-razor.md` + `docs/agent/reviews/2026-09-08-pr66-a65aa50d-razor-followups.md` (re-verified against current source; not chain-trusted)
- Target: PR 66 SHA `a65aa50dce560fd62e4c5e69db3b1c821bf08bd8` vs `origin/master` and vs prior tip `a2241db145ed2df36d4e83e91dfe1427b4297591`
- Mode: re-verify
- Model: grok-4.6
- Verdict: **ship** — F1 closed; no open ship-gate bugs

## Open bugs (must close before ship)

_None. Razor F1 closed at this tip; Final Boss found no new bug._

## Suggestions

- [ ] **F2** — Bound or close optional-length empty-skip sibling drift. File: `EffectLoweringPass.cs:410-417` vs `DomainEntityInstance.cs:193-198` / `DomainToCSharpExporter.Notify.cs:413-430`. Do: document assign-only empty-skip in the DSL guide, **or** teach Create/`ValidateConstraints` the same `!Required ⇒ skip null/empty` length rule so create and assign agree on `Text length(7,20)` + `""`.

- [ ] **F3** — Force void-export assign constraint sibling. File: `EffectLoweringPass.cs:447-451`. Do: OnEntry/ctor lowering (or export) test where constrained assign fails → `throw new InvalidOperationException(…)` (ctor/`OnEntry{Stage}` export) and destination not assigned after the throw in the tree. Runtime execute binds VM-shaped Failure (`RuntimeAnalysisCache.LowerStageBatchVm`) then `ThrowIfEffectListFailed` — say which body the test is proving.

- [ ] **F4** — Tighten same-tree export oracle. File: `ConstraintAssignLoweringTests.cs:209-239` (and soft ORs `:33`, `:63`). Do: build export via `DomainToCSharpExporter.LowerActionToMethodBody` (sets `ActionResultType`) and assert Failure + `must be >=` before assign on **both** module and export CS; drop soft `|| Failure` that lets throw-shaped trees pass.

- [ ] **F5** — Unique after constraints e2e. File: `EffectLoweringPass.cs:317-332`. Do: one property with Unique + Range (or Pattern); out-of-range fails before mutate without needing a peer; in-range duplicate hits EnsureUnique Failure and leaves prior value.

## Nits

- [ ] **N1** — Constraint check order parity. File: `EffectLoweringPass.cs:353-438`. Do: match declaration order used by Create/`ValidateConstraints`, or document fixed required→range→length→pattern precedence as intentional.

## Process

- [ ] **P1** — When copying Create-factory constraint IR into the shared Lower tree, diff against `ValidateConstraints` null/`is string` gates in the same PR (recurring class: export-shaped checks lack bag null skips). Prefer a shared helper or a checklist item in phenomenal-review Poly hooks. **Assign pattern now null-guards** (`EffectLoweringPass.cs:424-438`). Create factory (`Notify.cs:433-446`) still bare `IsMatch` — **reachable** on exported `Create` (`demo/Poly.RestApi/Patron.cs:130-131` `Regex.IsMatch(email, …)` with no null guard). File separately if desired; not a ship-blocker for this assign tip.

## Disposition of prior items (this SHA, independent re-read)

| ID | Razor @ a65aa50d | Final Boss @ a65aa50d |
|----|------------------|------------------------|
| **F1** | CLOSED | **CLOSED** — `And(NotEqual null, Not IsMatch)` @ `EffectLoweringPass.cs:424-438`; C# `&&`; VM `AndAlso`; test `PatternAssign_Null_DoesNotThrow_RetainsPriorValue` (`ConstraintAssignLoweringTests.cs:117-152`); ValidateConstraints `v is string` @ `DomainEntityInstance.cs:201-203` |
| **F2** | open suggestion | still open — `:410-417` unchanged |
| **F3** | open suggestion | still open — throw @ `:447-451`; ctor entry `DomainToCSharpExporter.cs:618-630`; no new test |
| **F4** | open suggestion | still open — soft ORs `:33`, `:63`; export pass still omits `ActionResultType` `:229-235` |
| **F5** | open suggestion | still open — order `:317-332`; no combo InvokeAction test |
| **N1** | open nit | still open — hardcoded order `:353-438` |
| **P1** | open process | still open; assign pattern addressed; Create factory still bare `IsMatch` and reachable |

## F1 close evidence (Final Boss re-verify)

- Diff `a2241db1..a65aa50d`: 2 files only (`EffectLoweringPass.cs`, `ConstraintAssignLoweringTests.cs`).
- `git show a65aa50d:Poly/DomainModeling/Lowering/EffectLoweringPass.cs` lines 424-438: null guard before `IsMatch`.
- Export/IR: `!= null && !IsMatch`; VM If uses `AndAlso`.
- Runtime: InvokeAction null → Failure pattern message + prior retained; no `ArgumentNullException`. Bag `Convert.ToString(object null) → ""` on this runtime (verified).
- Soft-skip vs Failure: true-null soft-skips (ValidateConstraints align); bag null→`""` yields Failure (documented).
- Local oracle: 2827 succeeded, 0 failed.
- Create-factory bare `IsMatch` remains residual, reachable, not this tip’s ship gate.
