# Round 5 — agent-a findings (slice: `for` fan-out invoke, OneToMany)

Probes: `probes/round5-agent-a/` — batch-billing.poly, inline-predicate.poly,
missing-params.poly, reject-edges.poly, reorder-engine.poly, reverse-side.poly.
All run through `scripts/run-probe.sh`; exports statically reviewed.

## F7 — wrong-typed `for` invoke argument passes analysis → export CS1503
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** for fan-out invoke / invoke argument typing
- **Repro:** `probes/round5-agent-a/batch-billing.poly` action `BadType`:
  `for lines as line where line Chargeable invoke line.Mark(amount: line Status)`
  — Text `Status` bound to Number param `amount`. Analysis accepts; export
  emits `target0.Mark(target0.Status)` → `error CS1503: Argument 1: cannot convert from 'string' to 'long'` (export line 293).
- **Expected:** wrong-typed invoke argument bindings are rejected at analysis
  (guide: "Expressions are type-checked at analysis"; fail-closed).
- **Actual:** analysis silent; export compile-fail. (Same class as round3 F1 for
  comparisons — invoke args were missed.)
- **Proposed patch:** type-check `invoke Action(param: expr)` bindings against the
  callee's declared parameter types in analysis (the parameter type resolution
  already exists for DTO-bound params).

## F8 — `for` fan-out invoking an entity-returning action from a void action → export CS0029
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** for fan-out invoke / return-type contract
- **Repro:** `probes/round5-agent-a/batch-billing.poly` action `Typed`:
  `for lines as line invoke line.CreateCopy()` where `CreateCopy: action -> LineItem`.
  Export emits `var result2 = target2.CreateCopy(); ... return result2;` —
  `DomainResult<LineItem>` returned from a `DomainResult` method → `error CS0029` (export line 314).
- **Expected:** invoking an entity-returning action as a fan-out effect from a void
  body is either rejected at analysis (DMEFF009-style: return value would be
  dropped) or the export unwraps/ignores the value. Fail-closed either way.
- **Actual:** analysis silent; export compile-fail; the created instances would be
  silently discarded even if it compiled.
- **Proposed patch:** analysis rejects `for ... invoke name.Action` (and plain
  `invoke Action`) when the target action declares `-> EntityType` and the caller
  is void — the return value has no consumer.

## F9 — reverse-side `for` diagnostic is misleading ("relationship does not exist")
- **Signal:** fail-loud-but-sharp
- **Severity:** 🟡
- **Slice:** for fan-out invoke / diagnostic quality
- **Repro:** `probes/round5-agent-a/reverse-side.poly` (Bin iterating `bins`,
  declared on Warehouse): analysis says `ForEachInvoke references relationship
  'bins' which does not exist on domain.`
- **Expected:** the relationship DOES exist — on `Warehouse`. The diagnostic should
  mirror the path-prefix wording: "relationship 'bins' is declared on source
  'Warehouse'; 'Bin' is not its source" (reverse-side invoke).
- **Actual:** misleading "does not exist" — an author would hunt for a missing
  declaration that exists elsewhere.
- **Proposed patch:** in the `for`/invoke relationship check, when the relationship
  resolves on the domain but the caller is not the source, emit a reverse-side
  message naming the actual source entity (matching the Q3′ path-prefix diagnostic
  style).

## Verified-OK in this slice (not findings)
- `for` fail-fast + zero-match: export `Go` emits matched-flag, fail-fast return,
  `DomainResult.Failure("for ... matched zero targets")` — matches runtime
  (DomainEntityInstance lines 740/763).
- Reject edges all fail loud with clear messages: OneToOne iteration, binder
  collision with caller member, nonexistent predicate policy, missing invoke param
  binding, inline (non-policy) predicate, self-relationship `for` (no recursion
  hazard), store-dependent predicate (see agent-c F11).
- `for` composition with assign/create-in/transition/nested-for in one body compiles
  and reads correctly.
