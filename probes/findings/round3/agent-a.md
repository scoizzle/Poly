# Discovery round3 — agent-a findings

Slice: DEGENERATE + CONTRADICTORY MODELS (empty/degenerate structures, contradictory
constraints, degenerate guards, duplicate names, self-relationships, contradictory
transitions).

Pipeline: automated path only (MCP disconnected). Every probe through
`scripts/run-probe.sh` (parse → analyze → export → Roslyn compile-check, 0 errors /
0 warnings gate). Runtime evidence from throwaway TUnit tests
(`Poly.Tests/DiscoveryAgentA3Runtime.cs`, 14 tests, all green documenting observed
behavior — deleted after). Other agents' broken throwaway files
(`DiscoveryAgentB2/`, `ZzRound3ControlFlowProbeTests.cs`, `ZzC3ControlFlowProbeTests.cs`,
`ProbeC3ControlFlowTests.cs`) were temporarily moved aside so the build could run and
restored after.

Probes (all under `probes/agent-a/`):
- `deg-empty.poly`, `deg-emptytypes.poly` — empty entity / stage / action / create
  initializers
- `deg-contradictions.poly`, `deg-contradictions2.poly` — reversed/contradictory
  range & length, default-outside-range, required+default
- `deg-guards.poly`, `deg-guards2.poly` — always-false/always-true policies gating
  actions, `require not` negation
- `deg-duplicates.poly`, `deg-selfref.poly`, `deg-selfref-one.poly`,
  `deg-actioncollide2.poly`, `deg-policycollide.poly`, `deg-navdup.poly`,
  `deg-navcollide.poly` — duplicate names, self-relationships, same-name actions
- `deg-transitions.poly`, `deg-entrytransition.poly` — entry overriding assigns,
  nested transition inside entry
- `deg-edges.poly`, `deg-nostages.poly` — transition-to-missing-stage, no-stages entity

---

## F1 — Same-name actions on multiple stages are silently mangled: every stage's body is appended to every same-named action; export emits duplicate methods (CS0111)

- **Signal:** compile-fail (divergence — runtime silently runs wrong bodies)
- **Severity:** 🔴
- **Slice:** same-name actions on multiple stages
- **Repro:** `probes/agent-a/deg-duplicates.poly` (Submit on Draft/Active/Done) →
  `error CS0111: Type 'Order' already defines a member called 'Submit' with the same
  parameter types` ×2. Throwaway TUnit: `Runtime_SameNameActionsOnStages_AllBodiesAccumulateOnFirstStageAction`.
- **Expected:** analysis rejects duplicate action names across stages (or the export
  disambiguates per stage); the runtime should run each stage's own declared body.
- **Actual:** `AddEffectToActionChange` resolves effects by name and appends to **every**
  stage action that shares the name (`DomainMutationContext.UpdateAction`, searchStages).
  Resulting structure: Draft.Submit=3 effects, Active.Submit=2, Done.Submit=1 (each later
  stage's body leaks into every earlier stage's action). Invoking Submit in Draft runs
  all three bodies (`Total=3`). Export cannot compile the model at all (CS0111). Analysis
  accepts silently.
- **Proposed patch:** reject duplicate action names across stages at analysis (or scope
  `AddEffectToActionChange` to a specific stage — the parser drops the stage context
  when emitting effect changes, `PolyDslParser.ParseActionBody:598`).

## F2 — Any entity with a `many` navigation to itself breaks the export: generated `Create{Rel}` passes `this` where a collection is expected (CS1503)

- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** self-relationships
- **Repro:** `probes/agent-a/deg-selfref.poly` (`Node { kids: many Node }`, no effects)
  → `error CS1503: Argument 1: cannot convert from 'Node' to 'System.Collections.Generic.IEnumerable<Node>'` at the generated `CreateKids()` (`Node.Create(this)`).
- **Expected:** a self-referencing `many` nav is a legal model (analysis accepts it) and
  should export a `create in kids` factory that builds a child with `parent = this` and
  an empty collection — or analysis should reject self-navigation fail-closed.
- **Actual:** the exporter's back-reference auto-wire (`Create{Rel}` factory) passes
  `this` for the collection parameter of the self-type's `Create(...)`. The private
  factory is emitted unconditionally, so **any** self-`many` nav fails compile even when
  no effect touches it.
- **Proposed patch:** in the exporter, when the relationship target is the source entity,
  don't double-pass `this` into the collection parameter (emit `Array.Empty<T>()` for the
  collection; keep `this` only for the to-one back-reference).

## F3 — Self-referencing to-one nav with `create in parent { }` emits a call to a nonexistent factory (CS1061)

- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** self-relationships / create-in on to-one
- **Repro:** `probes/agent-a/deg-selfref-one.poly` (`Node { parent: Node; Link: action { create in parent { } } }`)
  → `error CS1061: 'Node' does not contain a definition for 'CreateParent'`.
- **Expected:** the guide bans to-one bindings in `create in` initializers but a to-one
  nav as the `create in` target is still accepted by analysis; the export should either
  handle it (link via constructor param) or reject fail-closed at analysis.
- **Actual:** the analyzer does not reject `create in <to-one>` (only the initializer
  **binding** of a to-one nav is rejected per guide §0.3); the exporter emits a
  `CreateParent()` call that is never generated.
- **Proposed patch:** analysis should reject `create in` on a to-one nav with a clear
  diagnostic (DMEFF-style), or the exporter should emit the to-one factory.

## F4 — Entity-level + stage-scoped action with the same name: the stage body is silently dropped and leaks onto the entity action

- **Signal:** divergence / silent-gap (compile-fail)
- **Severity:** 🔴
- **Slice:** same-name entity/stage actions
- **Repro:** `probes/agent-a/deg-actioncollide2.poly`:
  ```
  Submit: action { assign Status to "entity" }
  Draft: stage { Submit: action { assign Status to "draft" } }
  ```
  → export `CS0111`. Throwaway TUnit: `Runtime_EntityAndStageSameNameAction_StageBodySilentlyDropped`
  (runtime: stage action = [assign "entity"], entity action = [assign "entity", assign "draft"]; invoking Submit in Draft → Status="entity", the "draft" body never runs).
- **Expected:** a stage-scoped `Submit` should override/scope the entity `Submit` with its
  own body, or analysis rejects the collision.
- **Actual:** the parser emits `AddActionToStageChange` (stage copy snapshots the entity
  action's current effects) followed by `AddEffectToActionChange` which attaches the
  stage's own body to the **entity** action by name (`DomainMutationContext.UpdateAction`
  prefers entity). Net: the stage-scoped body never runs, the entity action gains the
  stage body's effects, and the export emits two `Submit()` methods (CS0111).
- **Proposed patch:** same as F1 — reject same-name entity+stage action collisions at
  analysis, or route stage effect changes to the stage-scoped copy.

## F5 — An action and a policy sharing a name pass analysis and fail only at compile (CS0111 + CS0121)

- **Signal:** compile-fail (no analysis diagnostic)
- **Severity:** 🔴
- **Slice:** duplicate/colliding member names (cross-kind)
- **Repro:** `probes/agent-a/deg-policycollide.poly`:
  ```
  SameAsPolicy: action { assign Total to 1 }
  SameAsPolicy: policy { Total > 0 }
  ```
  → `error CS0111 ... already defines a member called 'SameAsPolicy'` + `error CS0121:
  The call is ambiguous between ... 'Order.SameAsPolicy()' and 'Order.SameAsPolicy()'`.
- **Expected:** analysis should reject cross-kind name collisions (action vs policy) with
  a clean "Duplicate member name" diagnostic, like property/property and policy/policy do.
- **Actual:** the "Duplicate member name" check (`Duplicate member name 'Total'`,
  `Duplicate member name 'DupPolicy'`) only fires within a kind. Cross-kind collisions
  surface as C# compiler errors at export.
- **Proposed patch:** extend the member-uniqueness diagnostic across action/policy/stage/
  property/nav names per entity.

## F6 — A `default` value outside its own `range`/`length` is accepted: the export factory permanently fails while the runtime silently stores the out-of-range value

- **Signal:** divergence / silent-gap
- **Severity:** 🟠
- **Slice:** contradictory constraints (default vs constraint)
- **Repro:** `probes/agent-a/deg-contradictions2.poly` (`Age: Number range(10, 20) default(5)`,
  `GtDefault: Number range(0, 10) default(50)`, `ShortDefault: Text length(5, 10) default("ab")`).
  Export: `Item.Create()` → `DomainResult.Failure("'Age' must be >= 10.")` — the default
  makes the entity's own factory dead-on-arrival with no args. Throwaway TUnit:
  `Runtime_DefaultOutsideRange_StoresOutOfRangeValue` — runtime `DomainEntityInstance.Create`
  stores Age=5L (out of range) with no validation.
- **Expected:** analysis rejects a default that violates the property's own constraint
  fail-closed; or both export and runtime apply-and-validate consistently.
- **Actual:** analysis accepts. Export validates the default through the range/length
  checks in `Create(...)` (returns Failure); runtime applies the default with no constraint
  check. The two paths diverge; neither surfaces a domain-level diagnostic.
- **Proposed patch:** a constraint-analysis check comparing `default` against `range`/
  `length` bounds (fail-closed diagnostic); the runtime should either validate defaults or
  the export should not run constraint checks on the defaulted-argument path.

## F7 — `create Child { }` (empty initializer) diverges: export fills CLR defaults ("" / false / 0L), runtime stores null

- **Signal:** divergence / silent-gap
- **Severity:** 🟠
- **Slice:** empty create initializers
- **Repro:** `probes/agent-a/deg-empty.poly` + `deg-emptytypes.poly`
  (`create Child { }`, `create in Many { }` on `Code: Text Num: Number Flag: Boolean`).
  Export: `Child.Create("", false, 0L)` / `CreateMany("", false, 0L)` — non-required,
  non-defaulted props are filled with `""`/`false`/`0L`. Throwaway TUnit:
  `Runtime_EmptyCreateInitializer_StoresNulls` / `Runtime_EmptyCreateInInitializer_StoresNulls`
  — runtime stores `null` for all three.
- **Expected:** an empty initializer leaves unset properties at the same value on both
  paths (per the guide, only `default(...)` sets values → null/undefined on both, or an
  explicit shared rule).
- **Actual:** the exporter substitutes CLR type defaults for unbound props
  (`EffectLoweringPass`), the runtime leaves them null. Same DSL, different stored values
  depending on path; an author writing the DSL can't predict which.
- **Proposed patch:** either have the export emit null/default! for unbound props or have
  the runtime apply CLR type defaults — one shared convention, verified by a cross-path test.

## F8 — A stage entry that contains a nested `transition` ends in a different stage: export ends in the outer target, runtime in the inner target

- **Signal:** divergence
- **Severity:** 🟠
- **Slice:** contradictory transitions (nested transition in entry)
- **Repro:** `probes/agent-a/deg-entrytransition.poly`:
  ```
  Active: stage { entry { transition to Done } }
  Done:   stage { entry { assign Status to "in_done" } }
  ```
  Export `Start()`: inlines `Status="in_done"; CurrentStage=Done;` **then** sets
  `CurrentStage=Active` → final stage **Active**. Throwaway TUnit:
  `Runtime_EntryContainingTransition_EndsInNestedTarget` → final stage **Done**,
  Status="in_done".
- **Expected:** export and runtime must reach the same end state for the same DSL (the
  runtime order is on-exit → set stage → on-entry; the export flattens entry effects
  before assigning CurrentStage).
- **Actual:** the exporter inlines the target stage's entry effects **before** the
  `CurrentStage = target` assignment (`CSharpCodeGenerator` stage-transition emission),
  so a nested transition's stage write is clobbered by the outer transition's stage write.
- **Proposed patch:** emit the entry-effect block **after** the `CurrentStage = target`
  assignment in the exporter (mirror the runtime order).

## F9 — Always-false entity-level policies silently gate every action (no analysis warning); `require Always` cannot rescue an action

- **Signal:** modeling-trap (faithful but surprising)
- **Severity:** 🟡
- **Slice:** degenerate policies gating everything
- **Repro:** `probes/agent-a/deg-guards.poly` / `deg-guards2.poly`:
  `Never: policy { false }` + `Pass: action require Always { … }`. Export and runtime both
  gate `Pass` with entity-level `Never` (export `Pass()` has `if (!this.Never()) fail`;
  runtime `DomainEntityInstance.cs:397` evaluates all entity-level policies) → `Pass` can
  never succeed. Throwaway TUnit: `Runtime_EntityLevelAlwaysFalse_GatesPassAction`.
  `require not Never` correctly opts out on both paths (`Runtime_NegatedEntityPolicy_SkipsEntityGate`).
- **Expected:** analysis flags an always-false policy (constant `false`) or an action
  gated by one, since it permanently dead-ends the action; the guide does document
  entity-level gating only implicitly.
- **Actual:** analysis is silent; the only observable failure is a guard-block at
  invoke/create time. Behavior is consistent export↔runtime, so it is a trap, not a
  divergence.
- **Proposed patch:** a constant-folding analysis warning for always-false policies
  (and always-true `require not` pairs).

## F10 — Duplicate stage names crash evolution with an opaque `ArgumentException` ("An item with the same key has already been added")

- **Signal:** fail-loud-but-sharp (opaque crash)
- **Severity:** 🟡
- **Slice:** duplicate names (stages)
- **Repro:** `probes/agent-a/deg-actioncollide.poly` — `DupStage: stage { }` declared twice
  → `Compilation failed: Evolution failed: An item with the same key has already been added. Key: DupStage`.
- **Expected:** a domain diagnostic like the duplicate-member/property/policy messages
  (`Duplicate member name 'X'`), naming the construct and stage.
- **Actual:** the stage-index dictionary throws an unhandled `ArgumentException` with a
  raw framework message; no domain-level error, and the failure mode is not greppable by
  construct name.
- **Proposed patch:** catch/lint duplicate stage names before the dictionary insert
  (mirror the "Duplicate member name" path used for properties/policies).

---

## Clean categories (verified, no findings)

- **Empty entity / empty stage / no-op action / entity with no stages** — all compile 0/0
  and behave reasonably (no-op action returns Success; no-stages entity has no CurrentStage).
  `deg-empty.poly`, `deg-nostages.poly`.
- **Empty policy `policy { }`** — correctly rejected at parse ("Expected expression, got '}'").
- **Reversed constraints `range(10, 0)` / `length(5, 1)`** — correctly rejected at analysis
  ("unsatisfiable RangeConstraint" / "unsatisfiable LengthConstraint bounds"). Verified at
  both runtime and export. `deg-contradictions.poly`.
- **Zero-range / zero-length (`range(0, 0)`, `length(0, 0)`)** — accepted, enforced, only
  0/"" satisfies; satisfiable degenerate, not a bug. `deg-contradictions2.poly`.
- **`required` + `default`** — consistent: the default satisfies the required check on both
  paths. `deg-contradictions2.poly`.
- **Duplicate property names / duplicate policy names** — rejected fail-loud with clear
  "Duplicate member name" diagnostics. `deg-duplicates.poly`.
- **Two navs with the same name on different source entities** (`Note.order` / `Comment.order`)
  — accepted and compile clean, matching the guide §4. `deg-duplicates.poly`.
- **Duplicate nav name on the same entity** — rejected fail-loud ("Relationship 'child' is
  defined more than once"). `deg-navdup.poly`.
- **`transition to Missing` stage** — rejected at analysis ("StageTransition effect targets
  stage 'Missing' which does not exist"). `deg-edges.poly`.
- **Entry/exit overriding an action's own assigns** (plain case, no nested transition) —
  consistent between export and runtime (both end with the entry's value); modeling trap,
  listed separately only as part of F8's ordering note. `deg-transitions.poly`.

---

## Repro index

- Probe files: `probes/agent-a/deg-*.poly` (10 files, listed above).
- Throwaway runtime evidence: `Poly.Tests/DiscoveryAgentA3Runtime.cs` (deleted; results
  recorded inline in F1/F4/F6/F7/F8/F9).
