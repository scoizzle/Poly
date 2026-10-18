# ADR: a `for` over zero records is a no-op

**Date:** 2026-10-17
**Status:** Accepted

## Context

The `for Rel as x [where …] invoke x.Action` fan-out lowered a `matched` flag plus a zero-check, so
the whole action **failed** whenever the loop body ran zero times:
`for {rel}.{action} matched zero targets.` One check produced two symptoms:

- a **predicate that matched nothing** — `for orders as o where o IsOverdue invoke o.Cancel` over a
  set with no overdue orders;
- an **empty collection** — a fan-out over a parent with no children (a scan over an empty file, a
  query with no sources).

The rule was deliberate and documented ("Zero matches fail (no vacuous success)"), inherited from the
August 2026 `for`-invoke design. In practice it made "nothing to do" indistinguishable from "failed":
the action returned `Failure`, the enclosing `for` failed by fail-fast propagation, and the effect
*after* the loop never ran — so a caller could not report "no matches" as a result.

The grep probe made the cost concrete (findings G3 in
`docs/probes/findings/grep-2026-10-17.md`): `grep x empty.txt` is an ordinary no-match (exit 1, no
output) and had no successful path in the language.

## Decision

**Zero records is a no-op.** The loop body runs once per matching record; when no record matches —
empty collection or filtered to none — the action continues with the effect after the `for` and
succeeds.

**Fail-fast is unchanged:** the first record whose invoke fails still fails the whole `for` and
returns that record's failure. There is still no transaction around the fan-out.

Scot, 2026-10-17, when asked whether to fix only the empty-collection case or both: *"Both — zero
iterations is never an error."*

## Rationale

- "No vacuous success" is a principle for **quantified predicates** (`all Rel where …` is false on an
  empty set, and stays that way); a `for` with side effects is an iteration, not a universal
  quantifier. Conflating them made the natural "collect, then act" shape fail on the ordinary case.
- The two symptoms are one observable (the body ran zero times); giving them different rules would be
  two rules for one fact.
- The modelling error the failure sometimes stood in for — *a collection nothing ever writes* — is
  already reported statically by **DMREL002** (`UnwrittenRelationshipPass`). The warning, not a
  runtime failure, is the right signal for it.

## Consequences

- `EffectLoweringPass.ForEachInvoke` emits only the fail-fast loop (no `matched` variable, no
  zero-check); the emitted locals renumber in the affected goldens (cosmetic).
- The guide (`Poly.Mcp/Docs/poly-dsl-guide.md` §6 and the effect table) and
  `docs/interpretation/domain-execution-model.md` state the new rule.
- Tests that asserted the failure flip to asserting success **and** that the effect after the loop
  runs; the university dogfood walk (`DropAllRegistered`, `CompletePassing` with nothing to do) is a
  real-domain confirmation.
- A subscription body still cannot carry a *failure return*: a `for` **or** a plain `invoke` inside a
  `when` emits `return DomainResult…` into a `void` handler (**CS0127**) — pre-existing, recorded in
  `docs/plans/poly-eng/research/2026-10-17-failure-return-in-void-subscription-handler.md`.
  The empty-set rule does not change that shape's status.
