# Discovery findings — discovery-for (`for` fan-out invoke surface)

Slice probes in `probes/discovery-for/`. Checked with `scripts/run-probe.sh` (0/0 bar) +
runtime via TUnit (MCP is stale, pre-`for`) + static export review.

## F-1 — `for` predicate referencing a store-dependent policy dead-ends the export
- **Signal:** fail-loud-but-sharp (export/runtime divergence)
- **Severity:** 🟠
- **Repro:** `probes/discovery-for/store-predicate.poly` (original): `for lines as line
  where line HasTag invoke line.Mark(amount: 1)` with `HasTag: policy { any tags where
  Label is "x" }`. Compiles 0/0; the generated predicate `target0.HasTag()` calls a policy
  method that **throws NotSupportedException** ("requires store-aware evaluation") — the
  whole action dead-ends at runtime on the first record. The runtime would evaluate the
  policy via the store (works).
- **Expected:** a `for` predicate must be lowerable to standalone C#; store-dependent
  policies rejected at authoring (fail-loud), not dead-end at runtime.
- **Actual:** the surface compiles and crashes.
- **FIXED:** `ValidateForEachInvoke` rejects a predicate policy containing quantifiers /
  path-prefix / exists with a clear message ("store-dependent … cannot be compiled to
  standalone C#"). `store-predicate.poly` now fails at analysis.

## F-2 — `for` binder-scoped args crash the runtime (binder root resolved as a relationship)
- **Signal:** compile-fail of the runtime path (runtime exception)
- **Severity:** 🔴
- **Repro:** `for items as item invoke item.Mark(amount: item Qty + Bonus)` at runtime →
  `InvalidOperationException: Relationship 'item' not found in domain`. The runtime's
  `PreprocessEffectExpressions` runs `PreprocessQuantifiers` over the `for` argument
  expressions, whose `RelationshipNavigation` override treats the binder root `item` as a
  store relationship (store-aware path-prefix) and fails.
- **Expected:** the binder root must be bound to the current target (in
  `ExecuteForEachInvoke` via `BindPeerInExpression`), never resolved as a relationship.
- **Actual:** any binder-scoped argument crashes the runtime (the export was fine).
- **FIXED:** `ForEachInvokeEffect` bindings skip the store-aware preprocessing (they carry
  no store quantifiers — analysis restricts roots to the binder); the binder is bound
  per-target. Covered by `ForEachInvoke_MixedBinderAndCallerArgs_ResolvesBoth`.

## Verified clean
- **Export loop edges:** two `for`s + a `for` nested in `if` + `transition` after →
  0/0, distinct loop locals (`target0/matched0/result0`, `target1/…`, `target2/…`), no
  CS0162 (`probes/discovery-for/export-edges.poly`).
- **Runtime fail-fast:** the first failing record throws; records after the failure are
  untouched; a record invoked BEFORE the failure keeps its mutation (the documented
  rollback gap) — `ForEachInvoke_FailFast_FailingTargetStopsAndPriorRemainMutated`.
- **Mixed binder + caller args** resolve correctly in both export
  (`target0.Mark(target0.Qty + this.Total)`) and runtime (Qty + Bonus → 15).
