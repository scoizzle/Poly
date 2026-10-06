# TASK PR113-fixes — Land every Razor finding on PR 113 as one PR

Status: open PR #117 @ `be223ba0`, CI pending.

PR: https://github.com/scoizzle/Poly/pull/117. Lane A, top priority. Implementer 100x, hand mill. Review: Final Boss. Parent: [PR113-review.md](PR113-review.md).
Findings: `docs/agent/reviews/2026-10-06-pr113-f398616e-razor.md` on `review/razor-pr113-f398616e` (addendum `ae6ff807`).

## Scope
One commit or a small group per fix-list item, with the finding IDs mapped to commits and tests in the PR's shape matrix:

1. B1, S9 part 2: action-local policies join the entity policy namespace.
2. B2, N1: guide section 12 carries the add/remove payload shapes; `SelectGuide` matches a section number exactly.
3. B3, S5, S6, S7: VM AST calls box scalars the same way for `new` and invoke; cache keyed by (node, mode), published after compile.
4. S1: one analysis refusal for Lower and Emit.
5. S2, S3, S4, N3: runtime policies and host jobs fail closed; one values bag per create.
6. S8, S9 parts 1 and 3, S11, N2: stale analysis on a session switch; doc claims.
7. S10: broken relative doc links fixed; `RelativeDocLinkTests` keeps them fixed.
8. S12: host-job nodes built once, with a benchmark. S13: one persistence-to-vendor swap.
9. S14: CURRENT restatements, inventory, THEN/PARKED overlap. N4: symbol cites.

## Done when
Every row fixed or explicitly waived; red-before tests where behavior changes; goldens untouched; full suite green; shape matrix and self-review sweep on the PR; hand-edit gate YES; Final Boss SHIP; merged.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-06 | 100x | hand | `be223ba0` | PR 117 filed: all Razor rows fixed, none waived | — |
