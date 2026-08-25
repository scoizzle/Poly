# Fleet-eval 2026-08-12 — 10-runtime findings (slice: runtime instance store)

Probes: `probes/fleet-eval/10-runtime/library.poly` (library: create-in, entity-level
subscriptions, peer binder, policy guards, constraints), `orders-fanout.poly` (for-invoke
fan-out + fail-fast, stage predicate, cross-entity invoke), `inventory-defaults.poly`
(constraints at create, `now`/`today`/`guid`/enum defaults). All three run 0/0 through
`scripts/run-probe.sh`. Runtime driven through the MCP product path (throwaway TUnit
`FleetEvalRuntimeProbeTests` in `Poly.Tests/Mcp/`, run with `--treenode-filter`, deleted
after — `git status` clean).

## F1 — `invoke_action` args are injected into the property bag unvalidated: real properties are deleted, guard policies can be bypassed
- **Signal:** runtime correctness + security (property/instance injection; export/analysis never accept this)
- **Severity:** 🔴
- **Slice:** invoke args / `InvokeAction` / policy guards (quality, security)
- **Repro:** throwaway TUnit (two tests):
  1. Domain `Item { Label: Text; Count: Number; Touch: action { } }`. `create_instance` with
     `{"Label":"orig","Count":3}`, then `invoke_action(Touch, {"Label":"clobbered","ghost":42})`
     → Success true, then `get_instance` snapshot shows only `Count` — **the `Label` property was
     permanently removed from the instance bag**.
  2. Domain `Vault { Age: Number; IsAdult: policy { Age >= 18 }; Enter: action require IsAdult { assign Age to Age } }`.
     `create_instance {"Age":15}` → `invoke_action(Enter)` blocked (guard). Then
     `invoke_action(Enter, {"Age":40})` → **Success true — the injected bag value shadowed the
     real `Age` and bypassed the `require IsAdult` gate** (and then removed `Age` from the bag).
- **Expected:** args are validated against the action's declared `Parameters` — unknown keys
  and keys that collide with entity properties fail closed (guide §6: "missing/duplicate
  action parameter bindings" are rejected; the MCP boundary should enforce the same contract
  DMEFF007 enforces for DSL `invoke`). Property values must survive an invoke untouched.
- **Actual:** `DomainEntityInstance.InvokeAction` (DomainEntityInstance.cs:386-404) injects
  every arg key into `_values` with no check, then `finally { _values.Remove(key) }` — a key
  matching a real property deletes it; a key matching a guard-relevant property bypasses the
  gate for the call duration. Both silent (Success true).
- **Proposed patch:** in `InvokeAction`, before injecting, reject arg keys not in
  `action.Parameters` (fail-closed) and never allow an arg key to shadow/delete a schema
  property; or inject into a separate parameter scope rather than `_values`.

## F2 — `create`/`create in` in entry effects: analysis accepts, the C# export compile-fails, the runtime silently orphans the child
- **Signal:** compile-fail + guide drift (guide §9 says `create`/`create in` are action-only) + silent gap (runtime orphan)
- **Severity:** 🔴
- **Slice:** entry effects / `create` / `create in` / store registration (quality, consistency, reliability)
- **Repro:**
  - `A { b: many B; Active: stage { entry { create in b { } } } }` (initial stage): `apply_dsl`
    accepts (F13 test), export compiles to C# then **CS1061 `'IReadOnlyList<B>' does not
    contain a definition for 'Create'`** (+1 warning). Runtime: `create_instance(A)` succeeds,
    child exists in `CreatedChildren`, but **not registered in the store and not linked** —
    `b exists` policy evaluates **false** (F14 test).
  - `entry { create B { } }` (initial stage): export **CS0111 duplicate ctor + CS0121**.
  - `entry { create B { } }` (non-initial stage): export **CS0117 `'B' does not contain
    'Create'` + CS0121**.
  - `entry { create in b { } }` (non-initial stage): analysis rejects on the export path with a
    misleading **"CreateIn effect references unknown relationship 'b'"** (b IS declared) —
    inconsistent rung vs the initial-stage form.
- **Expected:** authoring rung rejects `create`/`create in` in entry/exit uniformly (guide §9
  action-only). If it is meant to be shipped, the runtime must register+link the child (the
  export's ctor does run initial entry effects) and the export must emit compiling code.
- **Actual:** 3 of 4 sibling forms pass analysis and break the export at the C# compiler; the
  runtime creates an unreachable child (store/link silently skipped because
  `ApplyInitialStageEntryEffects` runs before the instance is in the store).
- **Proposed patch:** reject create/create-in in entry/exit at analysis (earliest rung), or
  lower them uniformly on both paths (register+link at Create; emit a real `Create{Nav}` call).

## F3 — MCP JSON boundary accepts type-confused property/param values (string into Number, number into Text param)
- **Signal:** divergence (runtime accepts what the export's typed Create factory / DTOs reject) + security
- **Severity:** 🟠
- **Slice:** `create_instance` / `invoke_action` value validation (consistency, security)
- **Repro:** throwaway TUnit:
  - `Item { Qty: Number range(0, 100) }` → `create_instance {"Qty":"not-a-number"}` → **Success
    true**; instance stores the string. `evaluate_policy` on `IsPositive: policy { Qty > 0 }`
    then fails loud: **"Evaluation failed: Cannot store a value of type 'String' in a numeric
    property."** — a poisoned instance that only surfaces at the next read.
  - `Tag: action (value: Text) { assign Label to value }` → `invoke_action(Tag, {"value":42})`
    → Success true, number stored into a Text property.
- **Expected:** `create_instance`/`invoke_action` coerce-or-reject values against the entity
  property / declared action parameter schema (export Create factories and DTO `[Range]` /
  `[MaxLength]` attributes reject at the boundary).
- **Actual:** `RuntimeTool.JsonElementToValue` + `DomainEntityInstance.Create` do no
  schema-type checking (constraints only catch the happy-typed violations). Values are stored
  as-is and fail (or miscompare) at a later, unrelated rung.
- **Proposed patch:** validate/coerce JSON values against the property's domain type in
  `Create`/`InvokeAction` (fail closed on wrong type), mirroring the export's typed factories.

## F4 — action invoked with a declared parameter but no args succeeds silently (writes null/garbage)
- **Signal:** silent gap (guide §6: "all params required"; fail-closed)
- **Severity:** 🟠
- **Slice:** `invoke_action` arg arity (quality, reliability)
- **Repro:** `Tag: action (value: Text) { assign Label to value }` →
  `invoke_action(Tag)` (no args) → **Success true**; `Label` is written from an unbound
  `value` (null/garbage) with no error.
- **Expected:** missing required action parameters fail loud (the DSL `invoke
  Action(param: expr, …)` shape is analysis-checked; the MCP `invoke_action` boundary should
  enforce the same "all params required" contract).
- **Actual:** no arity/required-param check in `RuntimeTool.InvokeAction` or
  `DomainEntityInstance.InvokeAction`; the missing param reads as an absent bag key and the
  assign proceeds silently.
- **Proposed patch:** in `InvokeAction`, when `action.Parameters` is non-empty, require every
  parameter to be bound in `args` (fail closed); reject extra keys (see F1).

## Independently confirmed (already filed by 06-subscriptions — not new)
- Subscription effect `create Fine { … }` (guide §0.4 canonical) crashes
  `NotifyTransition`'s `foreach (var subscriber in _instances)` with
  "Collection was modified; enumeration operation may not execute" — confirmed via the same
  MCP path (`invoke_action(GoOverdue)` on a linked Patron/Loan). Filed as 06-subscriptions F1.
- Multi-stage `all` never fires when the linked set is spread across the listed stages —
  confirmed by code reading (runtime `matchedCount` counts only `targetStageName`;
  DomainInstanceStore.cs:248-249). Filed as 06-subscriptions F2.

## Verified-OK in this slice (not findings)
- `for` fan-out fail-fast (first failing record throws; later records not invoked), zero-link
  and zero-match-after-predicate both throw.
- Subscription `Each` firing, peer binder scalar reads (`when loans Returned as loan { …
  loan Note }`), and stage-scoped `all` (single stage) quantifier semantics.
- Cross-entity invoke with no link throws; `Rel exists` empty → false / `not exists` → true.
- Constraint enforcement at create: out-of-range, pattern violation, and missing-required all
  throw on the MCP boundary.
- `default(now)`/`default(today)`/`default(guid)`/enum-member defaults resolve to typed values
  (DateOnly/DateTime/string/string) on create.
- Session isolation: per-session `Domain`/`InstanceStore`/`InstanceMap`; `Link` rejects
  cross-store instances; `apply_dsl`/evolve clears stale instances.
- Invoke depth (16) and transition depth (16) limits; rollback of already-invoked `for`
  records is the documented gap (guide §6).
