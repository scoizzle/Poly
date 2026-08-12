# Plan: `for`-invoke — cross-entity fan-out with named-policy/stage predicates

**Date:** 2026-08-12 · **Status:** approved design, in progress
**Owner:** this session · **Supersedes:** the current `invoke [any|all] Rel.Action [where …]` surface

## Problem

The current quantified cross-entity invoke has four defects:

1. **No binder** — `invoke all items.Mark(amount)` can't reference the current record, so
   arguments/effects are caller-side only (`invoke x.Mark(x.Qty)` is impossible).
2. **Ad-hoc `where` surface** — "target-local props/literals/comparisons/bool/arithmetic"
   is a parallel expression vocabulary the analysis can't deeply reason about (no
   verified-envelope hook).
3. **Fan-out confusion** — `any` (first success) vs `all` (fail on first failure) is a
   subtle distinction rarely matching intent; silent best-effort would be a footgun.
4. **Export dead-end** — quantified invoke lowers to `throw NotSupportedException`
   (discovery F1: CS0162 unreachable-code warning when followed by effects).

## Grammar (approved)

```
for Rel [as name] [where x.PolicyName | where x in StageName]
    invoke name.Action (param: expr, ...)
```

```poly
for lines as line where line.IsPaid
    invoke line.Mark(line.Qty)          # named policy on Line

for lines as line where line in Active
    invoke line.Mark()                  # Line is in stage Active
```

- **`for` always iterates every matching record** — no `all`/`any`/`each` keyword. One
  fan-out mode.
- **Binder (`as name`) is in scope** for the predicate and the invoke arguments.
- **Predicate must be a named policy OR a stage membership** — never an inline expression.
  Resolved on the **target entity** (the iterated record). `where x.Policy` = the target's
  policy as a bool; `where x in Stage` = `x.CurrentStage == TargetStage.Stage`.
- **Fail-fast:** the first record whose invoke fails fails the whole `for` (the action
  returns `Failure`). No silent swallow.
- **Zero matches fail** (no vacuous success — consistent with the guide's existing rule).
- **Rollback (undo of already-invoked records) is a documented gap**, not shipped: true
  atomic rollback needs store snapshots/undo; fail-fast already guarantees the caller
  always sees the failure. Recorded in this plan, not silently dropped.

## Semantics

| Aspect | Behavior |
|--------|----------|
| Fan-out | Every matching record, in storage order |
| Per-record failure | Fail-fast: action returns the failing record's `Failure` |
| Zero matches | `DomainResult.Failure("for … matched zero targets")` |
| Predicate | Target-entity named policy (bool) or stage membership (`in Stage`) |
| Binder scope | Predicate + invoke arguments |
| Export | Fail-fast loop over the nav collection (`this.Rel`), no throw |

## Analysis (shape rules, replace DMEFF007-era invoke checks)

- `for` requires a relationship (`Rel`) — no self-only `for`.
- `for` only on the relationship source, **OneToMany only** — iterating a singular
  (OneToOne) relationship is rejected (no meaningful iteration over a known single
  target).
- Predicate resolves on the target entity: policy exists on target, or stage exists on
  target. Fail-loud otherwise.
- Binder name must not collide with a property/stage/policy on the caller.
- `invoke name.Action` — `name` must be the binder; `Action` must exist on the target.
- Arguments: caller-side expressions + binder path-prefix (`x.Qty`).
- Reject: missing binder, missing/duplicate arg bindings, inline predicates,
  path-prefix/non-local predicates.

## Export lowering (fixes discovery F1)

```
for items as x where x.IsHighQty invoke x.Mark(x.Qty)
→
var matched = false;
foreach (var x in this.Items.Where(x => x.IsHighQty())) {
    matched = true;
    var result = x.Mark(x.Qty);
    if (!result.IsSuccess) return result;
}
if (!matched) return DomainResult.Failure("for items.Mark matched zero targets.");
```

- Policy predicate → the exported policy bool method. Stage predicate →
  `x.CurrentStage == ItemStage.Active`.
- No more `NotSupportedException` / unreachable-code (F1 cleared).

## Runtime alignment

`DomainEntityInstance.ExecuteInvokeEffect` currently has `any`/`all` fan-out. Replace the
quantified-invoke branch with the single fail-fast mode matching the above. Best-effort is
not a mode (rejected by design).

## Migration

- Remove `invoke [any|all] Rel.Action [where …]` from the grammar/guide.
- Update existing probes/models that use the old form (discovery-xinvoke probes, loans
  probe if it uses invoke).

## Acceptance

- `probes/discovery-xinvoke/invoke-orders.poly` (F1 repro) compiles **0/0** — no CS0162.
- New probe exercising: policy predicate, stage predicate, binder-scoped args, fail-fast on
  a failing record, zero-match failure.
- Full suite green; guide updated (invoke table + supported-effect summary + expression
  surface note); CORE.md invoke/analysis bullet updated.

## Build order

1. Parser: `for` keyword + grammar (binder, policy/stage predicate, invoke body).
2. Analysis: shape rules (target-entity predicate resolution, binder scope, DMEFF codes).
3. Export lowering: fail-fast loop (F1 fix), policy/stage predicate lowering.
4. Runtime: single fail-fast mode.
5. Remove old `invoke [any|all]` surface + update probes/guide/CORE.
6. Tests + full suite + commit.
