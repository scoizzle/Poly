# Discovery-c findings — round 2 (SUBSCRIPTIONS + ENTRY/EXIT + STAGE SCOPING + REQUIRE GATES)

Agent: `agent-c`. Protocol: [`docs/agent/poly-discovery-loop.md`](../../../docs/agent/poly-discovery-loop.md).
Round: 2 (findings to `probes/findings/round2/agent-c.md`).
Slice: `when Rel Stage [as name]` subscriptions (entity-level vs stage-scoped, peer binder,
quantifier-free Each), entry/exit effects (ctor, transitions, nested `if`), stage-scoped action
resolution + the "not found on entity" message, require/require-not gate composition with
entity-level policies, action parameter binding in invoke.

Probes (all pass `scripts/run-probe.sh` with 0 errors / 0 warnings):
- `probes/agent-c/library.poly` — Patron/Loan/Book: entity-level subscriptions (notification-only
  `when loans Loaned/Returned`, peer-binder `when loans Overdue as loan`), entry/exit effects,
  stage-scoped `MarkReturned`.
- `probes/agent-c/fulfillment.poly` — Customer/Order: stage-scoped subscriptions with peer binder
  (`when orders Shipped as ord`), entity-level `when orders Cancelled`, entry/exit with nested
  `if`/`else`, `require` / `require not` on stage-scoped actions, stage-scoped `ship` resolution.
- `probes/agent-c/insurance.poly` — Insurer/Claim: entity-level peer-binder subscription
  (`when claims Settled as c { paidOut += c Amount }`), entry/exit on initial stage in ctor,
  `require not` composition, action parameter binding (`assess(assessorName)`).

Runtime evidence: throwaway TUnit probes in `Poly.Tests/DomainModeling/Zz*ProbeTests.cs`
(19 tests, all run; deleted after use — tree clean).

---

## F1 — `"S" + Status` string concatenation: export compiles to working C#; runtime silently stores null
- **Signal:** export/runtime divergence (+ silent gap — no throw, no error)
- **Severity:** 🟠
- **Slice:** subscriptions + entry/exit effects (assign in action/entry bodies)
- **Repro:** `probes/agent-c/fulfillment.poly` — `Shipped: stage { entry { assign Code to "S" + Status } }`.
  Throwaway TUnit `StringConcat_Assign_ReturnsConcatenated` on `/tmp/strconcat.poly`
  (`assign Code to "S" + Status` with Status="paid"): `go` action succeeds but `GetProperty<object>("Code")` is `null`.
- **Expected:** export emits `this.Code = "S" + this.Status;` (verified in the generated C#, compiles
  and would produce `"Spaid"`); runtime should match or fail loud.
- **Actual:** the runtime VM path (`Interpreter.Compile` → `DirectVmAbiEmitter.EmitBinaryArithmeticValue`,
  `DirectVmAbiEmitter.cs:621-634`) has no string-concat arm — `Add` on two string heap handles falls
  through to raw `long` arithmetic and the assign stores `null`. The **LinqExpressions** path
  (`LinqExpressionGenerator.cs:536-540`) DOES handle `string.Concat`, so the "same DSL different VM"
  split is baked in. This silently corrupts any entry/exit/action assign that builds a text value,
  and is a live divergence: the export runs the concat correctly, the runtime no-ops it.
- **Proposed patch (not applied):** add a string-concat arm in `DirectVmAbiEmitter.EmitBinaryArithmeticValue`
  (mirror `CompileBinaryArithmetic`: when both sides are `HeapRef`/string-typed, emit
  `string.Concat` via `HeapValueToObject`), or reject `+` on text operands at analysis and fail loud.

## F2 — nested `transition to` inside an entry effect: export ends in the WRONG stage (and notifies the wrong subscribers)
- **Signal:** export/runtime divergence
- **Severity:** 🟠
- **Slice:** entry/exit effects (nested transitions)
- **Repro:** `/tmp/nestedentry.poly` — `Active: stage { entry { assign Status to "active" transition to Done } }`,
  action `go: Draft → Active`. Throwaway TUnit `NestedTransitionInEntry_StageOrdering`: runtime
  `InvokeAction("go")` ends at `CurrentStage == "Done"` (passes). Export:
  ```
  this.Status = "active";
  this.CurrentStage = ItemStage.Done;      // nested entry transition
  this.CurrentStage = ItemStage.Active;    // outer transition OVERWRITES it — final stage Active
  ```
- **Expected:** the DSL intent — `go` transitions Draft→Active, whose entry transitions Active→Done —
  must produce `Done` on both paths (runtime does).
- **Actual:** `EffectLoweringPass.StageTransition` (`EffectLoweringPass.cs:196-255`) inlines the target
  stage's entry effects BEFORE the `CurrentStage = target` assignment, so a nested transition inside
  entry sets `CurrentStage = Done`, then the outer assignment overwrites it back to `Active`.
  With subscriptions present (`/tmp/nestedentry2.poly`) the export additionally calls
  `NotifyDoneSubscribers()` for a stage the entity is about to leave, then re-assigns to `Active`.
  The runtime `TransitionStage` (`DomainEntityInstance.cs:652-670`) sets `CurrentStage = target` FIRST,
  then runs entry effects, so nested transitions win. Export/runtime disagree on the final stage.
- **Proposed patch (not applied):** in the export lowering, set `CurrentStage` to the target BEFORE
  running inlined entry effects (mirror the runtime order), or detect a nested transition inside
  entry/exit effects and fail loud.

## F3 — initial-stage entry effects: export runs them in the ctor; runtime `Create` does not — divergent initial state
- **Signal:** export/runtime divergence
- **Severity:** 🟠
- **Slice:** entry/exit effects (initial-stage ctor application)
- **Repro:**
  - `probes/agent-c/library.poly` — `Loan.Loaned.entry { assign Status to "loaned" }`.
  - `probes/agent-c/insurance.poly` — `Insurer.Active.entry { assign IsOpen to true }`.
  Throwaway TUnit `Library_InitialStageEntryEffect_RunsOnCreate` / `Insurance_InitialStageEntryEffect_RunsOnCreate`:
  `DomainEntityInstance.Create(...)` leaves `Status` null / `IsOpen` null.
- **Expected:** the export's ctor deliberately runs the FIRST stage's entry effects
  (`DomainToCSharpExporter.cs:511-531` — comment: "Apply the initial stage's entry effects in the
  constructor"), e.g. `this.Status = "loaned";`. The runtime should do the same on `Create` so a
  freshly created instance has identical state.
- **Actual:** runtime `DomainEntityInstance.Create` (`DomainEntityInstance.cs:97-149`) sets
  `currentStage = entity.Stages.FirstOrDefault()?.Name` but never executes `OnEntryEffects`. Any
  property initialized by the first stage's `entry` block (status stamps, `now`/`today` timestamps,
  IsOpen flags) is null at runtime but set in the export. A fresh object differs between paths.
- **Proposed patch (not applied):** in `DomainEntityInstance.Create`, after setting the initial stage,
  execute the first stage's `OnEntryEffects` through the same `TransitionStage`-style effect pipeline
  (with store-notify suppressed), or document the divergence explicitly in the guide.

## F4 — invoking a stage-scoped action from the wrong stage: runtime reports "Action 'X' not found on entity" (misleading); export says "requires stage"
- **Signal:** fail-loud-but-sharp (misleading error; runtime message claims the action does not exist)
- **Severity:** 🟡
- **Slice:** stage-scoped action resolution
- **Repro:** `probes/agent-c/fulfillment.poly` — `ship` is stage-scoped to `Paid`; a fresh Order is in
  `Pending`. Throwaway TUnit `Fulfillment_StageScopedAction_GatedFromWrongStage`:
  `order.InvokeAction("ship")` → `Succeeded=false`, `ErrorMessage == "Action 'ship' not found on entity 'Order'."`.
- **Expected:** the export emits a precise guard: `return DomainResult.Failure("'ship' requires stage
  'Paid' on entity 'Order'.")` (verified in generated C#). The runtime should say the action exists but
  is only valid in stage `Paid` — the action is not missing, it's stage-scoped.
- **Actual:** `TryResolveAction` (`DomainSemanticLookupExtensions.cs:127-149`) returns `action = null`
  when `currentStage`'s stage-actions don't contain the name AND no entity-level action matches, so
  `InvokeActionInternal` returns `ActionInvocationResult.Missing` (`DomainEntityInstance.cs:1345-1348`)
  → the MCP/user-facing message claims the action is not found on the entity. The action exists — it is
  stage-scoped elsewhere. Same action, two different failure stories between export and runtime.
- **Proposed patch (not applied):** in `TryResolveAction`, when the current stage misses but the action
  name exists in ANOTHER stage's `StageActions` (or the entity's full action set), return a
  "requires stage 'X'" failure instead of `null`, matching the export message.

## F5 — verified-OK (no finding): entity-level vs stage-scoped subscription gating and fan-out parity
- **Signal:** none
- **Severity:** n/a
- **Slice:** stage-scoped vs entity-level subscription placement
- **Repro:** throwaway TUnit `StageScoped_NotFiredWhenSubscriberNotInStage` and
  `Fulfillment_StageScopedSubscription_GatedOnCurrentStage`, `Library_EntityLevelSubscriptions_FireOnPeerTransition`.
- **Expected:** stage-scoped `when` fires only while the subscriber is in that stage; entity-level
  always fires; stage-scoped handlers run before entity-level.
- **Actual:** runtime matches the export (both gate on the subscriber's current stage; peer binder
  `ord Code` / `loan Code` / `c Amount` resolves to the transitioned peer on both paths; export emits
  `if (this.CurrentStage != CustomerStage.Active) return;` guards, `DomainToCSharpExporter.cs:451-457`,
  matching the runtime dispatch plan). No divergence found.

## F6 — verified-OK (no finding): require/require-not gate composition with entity-level policies
- **Signal:** none
- **Severity:** n/a
- **Slice:** require gate composition
- **Repro:** `probes/agent-c/insurance.poly` (`submit require HasAmount`, `settle require not HasAmount`)
  and fulfillment (`submit require HasSpend`). Throwaway TUnit `Insurance_HighValue_RequireNotBlocksApprove`,
  `Insurance_RequireGate_AndRequireNot_ComposeWithEntityPolicies`.
- **Expected:** `require not P` inverts the entity-level policy for that action; entity-level policies
  gate every action unless inverted.
- **Actual:** export (`DomainToCSharpExporter.cs:1143-1174`) and runtime
  (`DomainEntityInstance.cs:336-344`) agree: `require not P` skips the always-on entity-level `P` gate
  and adds the inverted guard; `FailedGuards` reports `not_HasAmount` on both paths. Note: the always-on
  entity-level gating itself is the round-1 F8 modeling trap (out of slice). No new divergence.
