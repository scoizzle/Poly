# Fleet-eval 2026-08-12 — Slice: Invariant & constraint propagation

Agent: fleet-eval (05-invariant)
Slice files: `Poly/DomainModeling/Analysis/EffectInvariantAnalyzer.cs`,
`ConstraintPropagationAnalyzer.cs`, `EffectAnalyzer.cs`, `AbstractValue.cs`,
`ConstraintValidation.cs`, `Constraints/ConstraintMerge.cs` (+ the storage/transport
consumers `StorageAnalyzer.ComputeVerifiedRanges`, `DbContextGenerator.CheckSql`,
`MinimalApiGenerator.GetPropertyRange/GetActionParamImplicitConstraints`).

Probes (all under `probes/fleet-eval/05-invariant/`):
- `warehouse.poly` — real system (bins/requests, range/length/pattern, conditional
  assigns, self-invoke chains, for-invoke with named-policy predicate). 0/0 PASS.
- `param-flow.poly` — unknown-envelope writers (unconstrained param, division).
  0/0 PASS (silently).
- `chainparam.poly` — call-chain argument binding (invoke Add(amount: 50)).
  0/0 PASS (silently).
- `binder-arg.poly` — fan-out binder-rooted arg (`invoke line.Scale(by: line Qty)`).
  0/0 PASS (silently).
- `openrange.poly` — one-sided ranges `range(100, )` / `range(, 50)`. COMPILE FAIL
  (false errors).
- `openrange-codegen.poly` — verified open-range codegen (CHECK + [Range]). 0/0 PASS.
- `policy-gate.poly` — mutually-exclusive entity policies. COMPILE FAIL.
- `deadstore.poly` — sequential dead store. COMPILE FAIL (false error).

---

## F1 — Parsed-DSL parameter references are `PropertyAccess`, not `ParameterAccess`: parameter-constraint analysis, call-chain argument binding, and param-flow warnings are dead on the product path
- **Signal:** silent-gap + guide-drift
- **Severity:** 🟠
- **Slice:** invariant & constraint propagation
- **Repro:** `probes/fleet-eval/05-invariant/chainparam.poly` — `DoIt: action { invoke Add(amount: 50) }`, `Add: action (amount: Number) { assign Total to Total + amount }`, `Total: Number range(0, 100)`. Also `param-flow.poly` `Adjust(delta)`/`Direct(amount)`.
- **Expected:** per guide ("the invoke argument bindings flow the bound expressions' value ranges into B's parameters"; "a callee assignment that can violate its target under the caller's context is reported as a diagnostic naming the chain") the binding `amount: 50` flows [50,50] into `Add`'s param, so `Total + 50 ∈ [50,150]` **can violate** range(0,100) → call-chain warning `DoIt → Add`. `assign Total to amount` should also raise the param-compatibility warning ("Parameter has no range constraint but flows to property").
- **Actual:** postconditions are `[?, ?]` (Unknown); zero warnings; the model compiles clean. `DslExpressionParser.ParsePrimary` (line 154) emits `DomainExpression.Property(name)` for bare identifiers, so `amount`/`bonus`/`delta` never become `ParameterAccess`. `EffectInvariantAnalyzer.Eval` consults `paramEnv` only for `ParameterAccess`; `EffectAnalyzer.ValidateAssign` gates the param-compatibility check on `ae.Value is ParameterAccess` (line 1099); `BuildPostconditionConstraints` gates on `value is ParameterAccess` (line 435); `ConstraintPropagationAnalyzer` gates on `expr is ParameterAccess` (line 186). All dead for parsed DSL. Meanwhile the export DOES bind the value at runtime (`this.Add(50L)`), so the write executes un-narrowed.
- **Side effect:** every parsed param used in an effect gets the false hint "Action parameter 'X' is declared but never referenced by any effect expression" (seen on `Restock/Sell/SafeSell/Settle/Adjust/Boost/delta/amount/by`).
- **Proposed patch:** resolve bare-identifier references to the in-scope action parameter in the analysis layer (e.g. a canonicalization pass converting `PropertyAccess(p)` → `ParameterAccess(p)` when `p` names a parameter of the containing action, or make `Eval`/`ValidateAssign`/`BuildPostconditionConstraints`/`ConstraintPropagationAnalyzer` fall back to the action's parameter list for PropertyAccess names that are not entity properties). Then `Eval` will consult `paramEnv` (the invoke/fan-out bindings) and the guide-promised diagnostics fire. Add regression tests via the PARSED path (the existing tests build `DomainExpression.Parameter(...)` programmatically and miss this).

## F2 — Unknown writer envelopes are treated as VERIFIED: DB CHECK constraints emitted where the invariant was never proved
- **Signal:** silent-gap (soundness of verified/declared distinction)
- **Severity:** 🟠
- **Slice:** invariant & constraint propagation (storage consumer `StorageAnalyzer.ComputeVerifiedRanges`)
- **Repro:** `param-flow.poly` + `scripts/run-probe.sh` then `dotnet run --project src/Poly.DslCompiler --mode db -- probes/fleet-eval/05-invariant/param-flow.poly` → emits `CK_qty "qty >= 0 AND qty <= 1000"` and `CK_balance "balance >= 0 AND balance <= 5000"`. Same for `warehouse.poly` `CK_qty "qty >= 0 AND qty <= 500"` (Bin.Qty written by `Restock(amount)`/`Sell(amount)`).
- **Expected:** per `StorageModel`/`DbContextGenerator` contract, a CHECK is emitted "only when the invariant analysis PROVED no effect can produce an out-of-range value". A writer whose postcondition `ValueRange` is null (unknown — unconstrained param, division, unknown arithmetic) means the envelope is NOT proved → the property must NOT be verified (fail-closed). The transport `[Range]`/CHECK should fall back to declared-only and, more importantly, the analysis should still flag the flows.
- **Actual:** `ComputeVerifiedRanges.Scan` skips postconditions with `post.ValueRange is not { } vr` (line 329) and the `violated` set stays empty, so the property is marked `verified=true` with the declared range → CHECK emitted. At runtime `Adjust(delta: 5000)` writes `Qty=5000` (the export has no range check in the action body) and the DB CHECK rejects at save — a write the analysis never certified and never warned about (F1 suppresses the warning).
- **Proposed patch:** in `ComputeVerifiedRanges`, track "any writer had an unknown/unprovable range" and treat it as not-verified (or, better, report it as a diagnostic). Unknown must not contribute `verified`. Add a regression test for `assign Qty to param` → `IsRangeVerified == false`.

## F3 — Null range bounds are converted to 0 (`Convert.ToDouble(null) == 0`): one-sided envelopes are corrupted into inverted ranges → false ERRORS on open-range models
- **Signal:** compile-fail (false rejection of valid models)
- **Severity:** 🟠
- **Slice:** `AbstractValue.cs` + `EffectAnalyzer.CheckDerivedRange`
- **Repro:** `probes/fleet-eval/05-invariant/openrange.poly` — `assign Balance to Balance - 50` on `Balance: Number range(100, )` and `assign Score to Score + 100` on `Score: Number range(, 50)`.
- **Expected:** `Balance - 50 ∈ [50, ∞)` can fall below 100 → **warning**; `Score + 100 ∈ (−∞, 150]` can exceed 50 → **warning**. Both are warning-level "can violate".
- **Actual:** `Compilation failed: Assigned expression value range [-50, 50] is entirely outside constraint range(100, +∞)` and `range [100, 150] is entirely outside constraint range(−∞, 50)`. Root cause: `AbstractValue.NumericRange` uses `ToDoubleOrNull` (line 85) without a null guard, so `Convert.ToDouble(null)` returns `0`, turning `range(100, )` into an inverted `(100, 0)` envelope; `EffectAnalyzer.ToDouble` (line 1335) does the same for the declared constraint's bound. `ConstraintValidation.IsInRange` and `StorageAnalyzer.ToValueRange` (line 367) both guard null correctly — the corruption is only in the abstract-interpretation path.
- **Latent crash note:** `EffectAnalyzer.ValidateCallChainPostconditions` line 1220 `double lo = vr.Min!.Value, hi = vr.Max!.Value;` will **NullReferenceException** the moment the F3 null-guard fix lets a genuine one-sided `ValueRange` through (e.g. `callchain-open.poly`: `Approve` invokes `UseBudget` which does `assign Spent to Budget` with `Budget: range(100, )`). The `!`-derefs must be made null-safe in the same change (use the same null-tolerant comparison as `CheckDerivedRange`).
- **Proposed patch:** add a null guard to `AbstractValue.ToDoubleOrNull` (return null when `v is null`), to `EffectAnalyzer.ToDouble`, and fix `ValidateCallChainPostconditions` to tolerate null bounds; extend `ComposeArithmetic` to compose one-sided intervals rather than returning `Unknown`. Regression test: `openrange.poly` should yield warnings (or nothing), never errors.

## F4 — Fan-out binder-rooted arguments (`invoke line.Mark(amount: line Qty)`) are evaluated against the CALLER's entity: binder-arg envelopes are Unknown, violations missed
- **Signal:** silent-gap
- **Severity:** 🟡 (instance of F1 with an additional root cause)
- **Slice:** `EffectInvariantAnalyzer.ApplyForEachInvoke` + `Eval`
- **Repro:** `probes/fleet-eval/05-invariant/binder-arg.poly` — `ScaleBig: action { for lines as line where line IsBig invoke line.Bump(by: line Qty) }`, `Bump: action (by: Number) { assign Qty to by + 50 }`, `Qty: range(0, 100)`, `IsBig: policy { Qty > 80 }`.
- **Expected:** `line Qty` resolves to the target's `Qty` narrowed by the predicate `IsBig` → `by ∈ [81, 100]`, so `by + 50 ∈ [131, 150]` is **entirely outside** range(0, 100) → call-chain error `ScaleBig → Bump` (guide: binder "is in scope for the predicate and the invoke arguments").
- **Actual:** `range=[?, ?]` (Unknown), zero diagnostics. Two causes: (a) the binding is `RelationshipNavigation` → `Eval(rn.TargetProperty, …, targetEntity: null)` — `ApplyForEachInvoke` (line 420) never threads the target entity into the binding `Eval`, so the leaf resolves against the CALLER's properties; (b) even if it resolved, the callee's `by` reference is a `PropertyAccess` (F1), so `paramEnv` is never consulted.
- **Proposed patch:** thread `targetEntity` into the binding `Eval` in `ApplyForEachInvoke` (and `ApplyCrossEntityInvoke`), and fix F1. Regression: the `binder-arg.poly` `ScaleBig` chain must report the violation.

## F5 — Entity-level policies are always-on invariants for every action: two mutually-exclusive predicate policies make the whole entity un-runnable
- **Signal:** modeling-trap ("entity-level policies gating every action")
- **Severity:** 🟡
- **Slice:** `EffectInvariantAnalyzer.CollectPreconditions`
- **Repro:** `probes/fleet-eval/05-invariant/policy-gate.poly` — `OrderLine` with `IsLarge: policy { Qty > 50 }` and `IsSmall: policy { Qty <= 40 }` (both authored as reusable fan-out predicates), plus `Mark` and `Reset` actions.
- **Expected:** named policies are guards/predicates; only `require`-gated (or documented always-on) policies should constrain an action. `Reset: action { assign Qty to 0 }` has nothing to do with either predicate.
- **Actual:** `Compilation failed: Action 'Reset' has unsatisfiable preconditions … narrowed property 'Qty' to an empty admissible set`, and the same for `Mark`. `CollectPreconditions` (line 97) concatenates ALL entity policies into every action's preconditions, so `Qty > 50 ∩ Qty <= 40 = ∅` rejects the whole entity. A single predicate policy (e.g. `IsLarge`) silently narrows every unrelated action's pre-state too (a subtler version of the same trap).
- **Proposed patch:** distinguish policies referenced by `require` / `for where` from always-on entity invariants (or document the trap loudly in the guide + add a diagnostic when two entity policies jointly narrow a property to an empty set while only being used as predicates).

## F6 — Dead-store false error: sequential assigns where the first is overwritten are rejected
- **Signal:** fail-loud-but-sharp (false positive)
- **Severity:** 🟡 (low)
- **Slice:** `EffectAnalyzer.ValidateAssign` / invariant postcondition per-effect
- **Repro:** `probes/fleet-eval/05-invariant/deadstore.poly` — `assign Qty to 200; assign Qty to 5` on `Qty: range(0, 100)`.
- **Expected:** the final stored value is 5 (in range); the exported code (`Qty = 200; Qty = 5;`) never persists the transient. At most a warning about the transient, ideally none.
- **Actual:** `Compilation failed: Assigned value '200' violates constraint range(0, 100)` — the dead first store blocks the whole domain. Per-effect verification has no liveness.
- **Proposed patch:** optionally carry an overridden-since flag through `ApplyEffects` so a later unconditional assign to the same target suppresses the earlier postcondition's violation (or document as a known limitation in the guide).

---

## Lens summary

- **quality:** F1 (param/call-chain narrowing dead), F2 (Unknown→verified), F3 (null→0 false errors), F6 (dead-store false error). Sequential intersection and per-branch conditional narrowing DO work (warehouse `TopUp` → [100,100] / [90,490]).
- **consistency:** storage CHECK and transport `[Range]` share the same (buggy) verified source; both use declared range, never the narrowed invariant envelopes (`CombinedRanges` is computed but never consumed by codegen). F2 makes the shared source unsound; F3 shows the invariant path and storage path disagree on null bounds (storage `ToValueRange` guards null, `AbstractValue` does not).
- **product:** out-of-range literal/derived assigns DO error/warn (existing tests + deadstore). Verified metadata reaches codegen (CHECK + [Range]) but is wrong under F1/F2; narrowed verified envelopes never reach codegen.
- **security:** under F1/F2/F4, out-of-range writes through params/binder args pass analysis silently and can reach storage (CHECK rejects at save → runtime failure, or persists out-of-range without a CHECK). The author is never told the invariant is unproven.
- **reliability:** unprovable envelopes fail CLOSED nowhere — they are treated as verified (F2) or silently skipped (F1/F4). Empty constraint handling (`AbstractValue.From([])` → Unknown) is fine. Latent NRE in `ValidateCallChainPostconditions` (line 1220) becomes reachable the moment F3 is fixed.
