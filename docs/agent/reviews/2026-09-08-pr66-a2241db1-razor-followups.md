# Follow-ups — PR 66 Item 6 assign constraints (Razor) — 2026-09-08

- Source review: `docs/agent/reviews/2026-09-08-pr66-a2241db1-razor.md`
- Target: PR 66 SHA `a2241db145ed2df36d4e83e91dfe1427b4297591` vs `origin/master`
- Mode: standard
- Verdict gate: **F1 must close before ship**

## Open bugs (must close before ship)

- [ ] **F1** — Null Text + `PatternConstraint` on assign must not throw. File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:424-436`. Do: guard `Regex.IsMatch` so null RHS yields `DomainResult.Failure("'…' does not match the required pattern.")` or skips like `ValidateConstraints` (`DomainEntityInstance.cs:201-203`) — never `ArgumentNullException`. Add InvokeAction test: pattern property, `code: null`, assert Failure (or documented skip), prior value unchanged, no throw. Note Create factory sibling hole at `DomainToCSharpExporter.Notify.cs:433-446` (same bare IsMatch); fix or file separately — assign tree is the ship gate.

## Suggestions

- [ ] **F2** — Bound or close optional-length empty-skip sibling drift. File: `EffectLoweringPass.cs:410-417` vs `DomainEntityInstance.cs:193-198` / `DomainToCSharpExporter.Notify.cs:413-430`. Do: document assign-only empty-skip in guide, **or** teach Create/`ValidateConstraints` the same `!Required ⇒ skip null/empty` length rule so create and assign agree on `Text length(7,20)` + `""`.

- [ ] **F3** — Force void-export assign constraint sibling. File: `EffectLoweringPass.cs:444-450`. Do: OnEntry/ctor lowering (or export) test where constrained assign fails → `throw new InvalidOperationException(…)` and destination not assigned after the throw in the tree.

- [ ] **F4** — Tighten same-tree export oracle. File: `ConstraintAssignLoweringTests.cs:172-203` (and soft ORs `:33`, `:63`). Do: build export via `DomainToCSharpExporter.LowerActionToMethodBody` (sets `ActionResultType`) and assert Failure + `must be >=` before assign on **both** module and export CS; drop soft `|| Failure` that lets throw-shaped trees pass.

- [ ] **F5** — Unique after constraints e2e. File: `EffectLoweringPass.cs:317-332`. Do: one property with Unique + Range (or Pattern); out-of-range fails before mutate without needing a peer; in-range duplicate hits EnsureUnique Failure and leaves prior value.

## Nits

- [ ] **N1** — Constraint check order parity. File: `EffectLoweringPass.cs:353-436`. Do: match declaration order used by Create/`ValidateConstraints`, or document fixed required→range→length→pattern precedence as intentional.

## Process

- [ ] **P1** — When copying Create-factory constraint IR into the shared Lower tree, diff against `ValidateConstraints` null/`is string` gates in the same PR (recurring class: export-shaped checks lack bag null skips). Prefer a shared helper or a checklist item in phenomenal-review Poly hooks.

## Disposition of prior items

No prior PR 66 review/follow-up files under `docs/agent/reviews/`. No prior open items to disposition.
