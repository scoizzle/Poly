# TASK PR113-review — Post-merge review of PR 113 (misc: shrink the agent surface, fail closed on a dirty lower)

Status: assigned 2026-10-06. Top-priority slice (Scot, 2026-10-06 06:12 CDT).

PR: https://github.com/scoizzle/Poly/pull/113. Squash-merged by Scot 2026-10-05 as `f398616e` without review. 184 files, +1491 / -1599.
Diff to review: `git diff 2e70e477..f398616e` (parent `2e70e477` = M1, #103).

## Who
- Reviewer: **Razor**, on the **OpenCode** mill (opposite the implementer's mill). Review 2 style first pass: exhaustive.
- Fixer: **100x** lands every accepted fix as **one PR** from current master.
- Then **Final Boss** reviews the fix PR → merge.
- After that merges, PR 110 (C1a) rebases onto master.

## What 113 changed (baseline everyone adopts)
- `get_dsl_guide` returns the short guide; `section` or `all` selects the rest. `add` and `remove` point at guide section 12 (contract and value-type payloads, DSL name rule).
- `docs/plans/simple-agent-tasks/PIPELINE-STATUS.md` is the only CURRENT.
- `Analyze` binds the compiling session first. `Lower` and `Emit` throw when analysis has errors.
- Uncalled `DomainExpressionLoweringPass` helpers under runtime are gone.

## Review output
One findings table: id, severity (bug / suggestion / nit), file:line, what is wrong, fix. Answer the hand-edit gate: could Scot change this by hand, opening the files cold? Note anything that conflicts with merged slices #101, #104, #105, #106, #108, #111, #112 that landed after 113.

## Done when
Razor's table is posted (PR comment on #113 or a review file linked here); 100x's fix PR closes or explicitly waives each row; Final Boss SHIP; merged.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-06 | Foreman | — | `f398616e` | Review assigned to Razor | — |
