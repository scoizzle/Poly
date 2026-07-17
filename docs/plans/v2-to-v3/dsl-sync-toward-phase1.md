# DSL-Engine Sync Plan — Toward Phase 1

**Date:** 2026-07-16  
**Status:** Execution plan  
**Source:** [`docs/experiments/domain-modeling-dsl-tour-feedback.md`](../../experiments/domain-modeling-dsl-tour-feedback.md) — §3 and §4 recommendations  
**Trigger:** IR/DSL divergence (events vs stage-observation); mutation surface width; single-vertical runtime gap  
**Related:**

| Doc | Role |
|-----|------|
| [`DOMAIN-DSL-SPEC.md`](../../experiments/DOMAIN-DSL-SPEC.md) | DSL vision (design laboratory, not build checklist) |
| [`vertical-slice-finish-plan.md`](vertical-slice-finish-plan.md) | Post-M2 expansion (this plan extends with IR surgery & DSL) |
| [`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md) | MCP + DSL complementary paths |
| [`../CORE.md`](../../CORE.md) | Platform mechanisms (no reinvention) |
| [`../../AGENTS.md`](../../AGENTS.md) | Principles, placement, build/test |

---

## 1. What is out of sync

The tour feedback identifies four concrete gaps between the DSL direction (v0.3) and the current engine IR.

### Gap 1: Event-centric IR vs stage-observation DSL

| DSL (v0.3 settled) | Engine today | Migrate to |
|--------------------|-------------|------------|
| Stage transitions are the observable | First-class `Event` record + `PublishEventEffect` + `EventSubscription` + `EventCorrelationBinding` + `EventSubscriptionRoutingMode` | Stage transition is the only first-class observable; ~5 event analyzers adapt or shrink |
| `when property Stage { effects }` | `CorrelationAnalyzer`, `EventContractAnalyzer`, `ReplaySafetyAnalyzer`, `EventFlowAnalyzer`, `CausalityAnalyzer` | Stage-transition subscription replaces event publish/subscribe |
| `Entity.Events` references | 4 `DomainChange` types for events (`AddEventChange`, `RemoveEventChange`, etc.) | Deprecate or consolidate to engine-internal |
| Correlation via relationship path | `EventCorrelationBinding` with event-property/consumer-property pairs | Relationship-path binding is implicit — no correlation keys needed |

**Size of event IR to reconcile:**

| Artifact | Count | Action |
|----------|-------|--------|
| Event record file | 1 | Reduce to internal type or remove |
| PublishEventEffect | 1 | Deprecate — promote to engine-internal observation |
| EventSubscription record | 1 | Replace with stage-subscription model |
| EventSubscriptionRoutingMode enum | 1 | Remove |
| EventCorrelationBinding record | 1 | Remove |
| Entity.Events property | 1 usage | Remove from public surface |
| Entity.EventSubscriptions property | 1 usage | Replace with stage subscriptions |
| Event-centric DomainChange types | 4 | Deprecate/remove |
| Event-centric analyzers | 4–5 | Adapt to stage-observation or remove |
| Event-centric diagnostic codes | 6 | Merge into stage-observation equivalents |
| Causality graph (event-based) | 1 analyzer | Rewire to action→stage→subscription graph |

### Gap 2: No Phase 1 DSL path

The DSL spec defines a Phase 1 surface (`SupplyChain.poly`) and a v0.3 settled shape (`phone-call.poly`, `order-fulfillment.poly`). There is no parser, no importer, no exporter, and no `.poly` file format to commit to VCS. The MCP path exists but is incremental — there is no batch equivalent.

### Gap 3: Mutation surface wider than DSL entity block

30+ `DomainChange` types exist today. A single DSL entity block (e.g. `Product: entity { ... }`) would need 20+ MCP tool calls or 20+ `DomainChange` records to reproduce. There is no `AddEntityBlockChange` that takes a full entity definition as a unit.

### Gap 4: No closed runtime loop for one vertical

The DSL spec models action→effects→stage transition→reactive `when` as a coherent loop. The engine has:
- `DomainExpression` → Syntax lowering (complete, tested)
- Effect lowering → `null` (partial — assign/composite/conditional only — intentional transitional state)
- Stage→effect execution → `DomainEntityInstance` only (not generic AST/VM)
- `When` subscription → not executed at all

Two execution stories exist (generic AST/VM for expressions, `DomainEntityInstance` for effects). Neither is a closed loop for the DSL semantics.

---

## 2. Principles for this plan

1. **Fix the IR first** — do not build a Phase 1 DSL parser against an event-centric IR that the DSL has already abandoned. Resolve Gap 1 before starting Gap 2.
2. **One open gap at a time** — the four gaps are sequential dependencies, not parallel workstreams. Close each before opening the next.
3. **Keep existing analyzers healthy while migrating** — event analyzers can stay in reduced form until their replacement passes exist. Do not orphan tests.
4. **No new IR types without a lowering path** — every new concept must have a plan for reaching generic Syntax nodes.
5. **Phase 1 freeze means Phase 1 freeze** — `parallel`, `schedule at`, `for`, full actor auth, match expressions, collection query DSL, domain kinds (`service|cli|library`) are not Phase 1.

---

## 3. Execution plan

### Phase 0: Survey & inventory (0.5 day)

Before any code changes, produce a precise bill of materials:

- [ ] **P0.1** Catalog every `Event`-related type, record, property, and usage in the engine (files, lines, callers, tests).
- [ ] **P0.2** Catalog every analyzer pass that depends on `Event`, `EventSubscription`, `PublishEventEffect`, and classify each as:
  - **Dead** — no product path depends on it; remove
  - **Adaptable** — logic can be re-expressed against stage transitions
  - **Engine-internal** — useful for the lowerer but not part of authoring
- [ ] **P0.3** Catalog every DomainChange type that references events, and decide: remove, deprecate, or promote to engine-internal.
- [ ] **P0.4** Catalog every test (TUnit) that depends on event machinery — count and classify.
- [ ] **P0.5** Determine the minimum viable `DomainMutationContext` surface needed for stage-subscription model.

**Exit:** A published catalog (could be added to this plan's appendix or a separate tracking doc) + no code changes yet.

---

### Phase 1: Collapse event IR → stage-observation (2–3 days)

The highest-leverage change. The goal is NOT to remove every event reference overnight — it's to make stage transitions the **primary** observable, and demote events to an engine-internal detail or remove them entirely.

#### 1a. Introduce stage-subscription model to the IR (1 day)

- [ ] **1a.1** Add `StageSubscription` record to replace `EventSubscription`:
  ```csharp
  public sealed record StageSubscription(
      string RelationshipPath,    // e.g. "payment" or "items"
      string StageName,           // target stage name
      string? Quantifier,         // null, "any", "all"
      IReadOnlyList<Effect> Effects
  ) : DomainObject;
  ```
- [ ] **1a.2** Add `StageSubscription` collection to `Stage` record (alongside `OnEntryEffects`, `OnExitEffects`).
- [ ] **1a.3** Update `Entity` record: keep `EventSubscriptions` as obsolete/deprecated shim, add forwarder to stage-collected subscriptions.
- [ ] **1a.4** Add `EntitySubscriptions` query helper on `DomainQueries` or analyzer metadata that flattens all stage subscriptions across all stages.
- [ ] **1a.5** Deprecate `EventSubscription` record with `[Obsolete]` (or doc-comment deprecation). Do not remove yet.
- [ ] **1a.6** Deprecate `EventCorrelationBinding` and `EventSubscriptionRoutingMode`.

**Exit:** Stage-subscription record exists. `Entity.EventSubscriptions` is still populated by migration path or remains for existing callers. All existing tests pass unchanged.

#### 1b. Deprecate PublishEventEffect in favor of stage observation (0.5 day)

- [ ] **1b.1** Mark `PublishEventEffect` as deprecated in doc comment.
- [ ] **1b.2** Add `StageTransitionEffect` as the canonical way to express observable transitions — already exists, confirm it's sufficient.
- [ ] **1b.3** Verify that any path that previously published an event can express the same semantics as: `transition to StageName` + subscription effects on the consumer side.

**Exit:** No new code uses `PublishEventEffect`. Existing callers still compile with deprecation notice.

#### 1c. Collapse event-centric DomainChange types (0.5 day)

- [ ] **1c.1** Remove `AddEventChange` / `RemoveEventChange` from the product mutation surface — events are no longer top-level authorable types.
- [ ] **1c.2** Remove `AddEventReferenceToEntityChange` / `RemoveEventReferenceFromEntityChange` — no longer needed.
- [ ] **1c.3** Add `AddStageSubscriptionChange` / `RemoveStageSubscriptionChange` for the new stage-subscription model.
- [ ] **1c.4** Update MCP tools that previously exposed event-surface (check `V3DomainTools.cs`).

**Exit:** Fewer DomainChange types. MCP tools reflect stage-subscription model.

#### 1d. Adapt event-centric analyzers (1 day)

The analyzers that depend on event machinery need to be adapted or deprecated:

| Analyzer | Current role | Action |
|----------|-------------|--------|
| `EventFlowAnalyzer` | Warns on unpublished events, unbound event properties | **Remove** — stage transitions are always "published" by definition |
| `CorrelationAnalyzer` | Validates event-subscription correlation bindings | **Remove** — correlation is implicit via relationship path; no bindings to validate |
| `EventContractAnalyzer` | Validates event handler parameter contracts | **Adapt** — validate that subscription effect parameters match the source entity's properties |
| `CausalityAnalyzer` | Detects cycles in event-driven action chains | **Adapt** — detect cycles in stage-subscription → action → stage-transition chains |
| `ReplaySafetyAnalyzer` | Detects non-idempotent event handlers | **Adapt** — same logic applies to subscription-triggered effects |
| `IdempotencySafetyAnalyzer` | Simple heuristic on action names | **Keep** — entity-agnostic, still valid |
| `EffectAnalyzer` | Validates effect bindings | **Keep** — entity-agnostic |
| Others (structural, constraints, etc.) | Entity-agnostic | **Keep** — no changes |

- [ ] **1d.1** Remove `EventFlowAnalyzer` and its diagnostic codes (`EventFlowLiveness` [DMEV002]).
- [ ] **1d.2** Remove `CorrelationAnalyzer` and its diagnostic codes (`EventCorrelationSoundness` [DMEV004]).
- [ ] **1d.3** Adapt `EventContractAnalyzer`: rename to `SubscriptionContractAnalyzer`, validate that subscription effects reference valid entity properties on the target type.
- [ ] **1d.4** Adapt `CausalityAnalyzer`: rewire from event→subscription graph to action→stage→subscription graph.
- [ ] **1d.5** Adapt `ReplaySafetyAnalyzer`: rewire from event subscriptions to stage-subscription-triggered effects.
- [ ] **1d.6** Update `DomainModelDiagnosticCodes`: remove `DMEV002`, `DMEV004`; renumber/repurpose `DMEV001`, `DMEV003`, `DMEV005`, `DMEV006` for stage-subscription equivalents.
- [ ] **1d.7** Update `DomainModelAnalysisBuilderExtensions` pass registration to reflect new/changed analyzers.

**Exit:** 2 analyzers removed, 3 adapted, 4 kept. All pass registration updated. Tests for removed analyzers are deleted; adapted analyzers have updated tests.

#### 1e. Clean up the Event record (0.5 day)

- [ ] **1e.1** Decide: does `Event` record remain as an engine-internal type (used by lowering to represent "a stage transition as data") or is it fully removed?
  - **Recommendation:** Keep as engine-internal. The lowerer may still need a typed representation of "entity X transitioned to stage Y at time Z" for codegen and introspection. It should not be authorable from the DSL.
- [ ] **1e.2** If kept: move to `Poly.DomainModeling.Internal` namespace or mark with `[EditorBrowsable(Never)]`.
- [ ] **1e.3** Remove `Entity.Events` property — event references are no longer on entity surfaces.
- [ ] **1e.4** Remove `Entity.EventSubscriptions` property (after migrating to stage-based subscriptions).

**Exit:** Event record exists only as an engine-internal detail. `Entity` no longer has `Events` or `EventSubscriptions` properties.

#### Phase 1 tests

- [ ] **1t.1** Existing tests that create events via `DomainChange` — update to use stage subscriptions or remove.
- [ ] **1t.2** Existing tests that check event-related diagnostics — update for new analyzer diagnostics.
- [ ] **1t.3** Verify no existing domain tests (Order, Phone Call, etc.) reference event infrastructure.
- [ ] **1t.4** Full test run green before moving to Phase 2.

---

### Phase 2: Freeze Phase 1 DSL surface & implement parse→evolve→print (3–5 days)

Only start after Phase 1 exit criteria are met. The IR must be stable (stage-observation model) before building the parser against it.

#### 2a. Define the Phase 1 grammar (0.5 day)

Promote the Phase 1 surface from the DSL spec into a frozen grammar:

**Phase 1 includes:**
- ✅ `domain Name` header (no kind suffix — default `service`)
- ✅ Entity, Value type declarations
- ✅ Properties with primitive types: `Text`, `Number`, `Boolean`, `DateTime`, `Date`
- ✅ Constraint modifiers: `required`, `unique`, `range(min, max)`, `length(min, max)`, `pattern(regex)`
- ✅ Stage declarations with entry/exit blocks
- ✅ Action declarations inside stages (zero-ceremony `Name: {}` and full `Name: action { }`)
- ✅ `transition to StageName` effect
- ✅ `assign property to expr` effect
- ✅ `create Entity { props }` effect
- ✅ `create in relationship { props }` effect
- ✅ `when property Stage` subscriptions on stages
- ✅ `when any/all property Stage` quantifiers for collection subscriptions
- ✅ `require PolicyName` / `require not PolicyName` on actions
- ✅ Policy declarations with expressions: comparisons, `and`/`or`/`not`, literals, property references
- ✅ Relationships as entity-typed properties: `property: many owned Target`, `property: Target`
- ✅ Stage gates (`when StageName` on actions)

**Phase 1 explicitly excludes:**
- ❌ `actor` keyword (Phase 2 — still lowers to entity)
- ❌ Entity extension (`Name: Parent { }`)
- ❌ `schedule at` (time-based effects)
- ❌ `for` iteration
- ❌ `parallel` fork/join
- ❌ Match expressions
- ❌ Collection query DSL (`.all()`, `.any()`, `.count()`, etc.)
- ❌ Static member references (`DateTime.Now`)
- ❌ `external` policies
- ❌ `domain Name: kind` (defaults to `service`)
- ❌ Library import/export
- ❌ Functions on entities
- ❌ `invoke target.Action()` (cross-entity action calls)
- ❌ `start EntityName()`
- ❌ `event` / `publish` / `subscribe` (replaced by stage subscriptions in Phase 1 IR)

- [ ] **2a.1** Publish a frozen Phase 1 grammar as a single authoritative doc (can be a sub-page of the DSL spec or a new `docs/plans/dsl-phase1-grammar.md`).
- [ ] **2a.2** Define the `DomainChange` sequence that each DSL construct maps to (e.g. an entity block becomes: `AddEntityChange` + 1× `AddPropertyToEntityChange` per property + 1× `AddStageChange` per stage + ...).

#### 2b. Build the DSL parser (2–3 days)

- [ ] **2b.1** Implement a `PolyDslParser` class (under `Poly/DomainModeling/Parsing/`) that:
  - Reads `.poly` text
  - Produces `IReadOnlyList<DomainChange>`
  - Reports parse errors as diagnostics (not throws)
  - Uses System.IO.Packaging or custom text scanner — no parser-generator dependency (per platform principle: zero external deps)
- [ ] **2b.2** Implement `AddEntityBlockChange` — a single DomainChange that encodes a full entity definition (all properties, stages, actions, policies, stage subscriptions). This is the key higher-level change that shrinks the mutation surface (Gap 3).
  - `AddEntityBlockChange` applies by decomposing into individual changes internally in `ApplyTo`. This keeps the evolution gate operating at the same granularity while reducing MCP round-trips.
- [ ] **2b.3** Set up parsing test infrastructure: one `.poly` test file per entity, round-trip through `parse → evolve → analyze`. Verify diagnostics pass.
- [ ] **2b.4** Parse the Phase 1 examples (`SupplyChain.poly`-equivalent) and verify they produce the expected domain.

#### 2c. Build the canonical printer (1 day)

- [ ] **2c.1** Implement `DomainDslPrinter` (under `Poly/DomainModeling/Parsing/`) that:
  - Reads a committed `Domain`
  - Produces stable, deterministic `.poly` text
  - Normalizes constraint syntax to named form (`range(min: 0)` not positional `range(0)`)
  - Outputs entities in deterministic order (alphabetical or original-declaration order with dep-first)
  - Outputs stages in their declared order
  - Strips engine-internal details (no event output)
- [ ] **2c.2** Round-trip test: `parse → evolve → analyze → print → parse → evolve → analyze`. Verify the second parse produces an identical domain (structural equality, not text equality).
- [ ] **2c.3** Verify that MCP export (`get_domain_snapshot` → DSL) produces valid `.poly`.

#### 2d. DSL import tool (0.5 day)

- [ ] **2d.1** Add `apply_dsl` MCP tool that accepts `.poly` text, parses, evolves, analyzes, returns diagnostics + new domain snapshot.
- [ ] **2d.2** Add Capture mode variant: `apply_dsl strictness: "capture"` commits even when analysis finds errors (per MCP expansion plan).
- [ ] **2d.3** Wire into `McpSessionStore` and `V3DomainTools`.

#### 2e. Phase 2 tests

- [ ] **2t.1** Parse all four worked examples (`phone-call.poly`, `order-fulfillment.poly`, `franchise-crm.poly`, `grep.poly`). Verify that Phase 1 constructs parse and Phase 2+ constructs produce clear "not yet supported" errors (not crashes).
- [ ] **2t.2** Round-trip stability: 10 randomly generated entity definitions → parse → print → verify structural identity.
- [ ] **2t.3** Error-path tests: malformed syntax, unresolved references, duplicate names, constraint type mismatches.
- [ ] **2t.4** MCP `apply_dsl` smoke test: submit `.poly` text via MCP, verify domain state matches.
- [ ] **2t.5** Full test run green.

---

### Phase 3: Shrink mutation surface (1 day)

This phase reduces the number of `DomainChange` types exposed to MCP/DSL consumers, replacing micro-changes with block-level changes that match DSL entity blocks.

**Current surface:** ~30 `DomainChange` types (individual add/remove for entity, property, stage, action, policy, effect, constraint, relationship, event, parameter, stage-subscription, etc.)

**Target surface for Phase 1:**

| Change | Granularity | Replaces |
|--------|------------|----------|
| `AddEntityBlockChange` | Full entity definition | ~15 micro-changes per entity |
| `RemoveEntityChange` | Entity-level | (existing, keep) |
| `AddRelationshipChange` | Relationship-level (existing) | Keep — simpler than inline entity property for MCP |
| `RemoveRelationshipChange` | Relationship-level (existing) | Keep |
| `SetDomainNameChange` | Domain-level (existing) | Keep |
| `AddValueTypeChange` | Value type-level (existing) | Keep but consolidate into block |
| `RemoveValueTypeChange` | Value type-level (existing) | Keep |

The MCP tools (and the DSL parser) use these block-level changes. The old micro-changes remain available for the programmatic `DomainEvolution` API but are removed from the MCP tool surface.

- [ ] **3.1** Implement `AddValueTypeBlockChange` (mirrors `AddEntityBlockChange` for value types).
- [ ] **3.2** Remove micro-DomainChanges from MCP tool surface: `add_property`, `remove_property`, `add_stage`, `remove_stage`, `add_action`, `remove_action`, `add_policy_to_entity`, `add_policy_to_stage`, `add_policy_to_action`, `remove_policy_*`, `add_constraint`, `remove_constraint`, `add_parameter`, `remove_parameter`, `add_effect_to_action`, `add_on_entry_effect`, etc.
  - These remain in the `DomainChange` hierarchy for programmatic use — only removed from MCP tool registration.
- [ ] **3.3** MCP tools now reflect only block-level operations: `add_entity_block`, `remove_entity`, `add_relationship`, `remove_relationship`, `add_value_type_block`, `apply_dsl`.
- [ ] **3.4** Update `V3DomainTools` registration — remove deprecated micro-tools, add new block tools.
- [ ] **3.5** Update MCP README to reflect the reduced surface.

---

### Phase 4: Close one runtime loop — action→effects→stage→subscription (3–5 days)

The hardest phase. The goal is **one end-to-end vertical slice** where the full DSL semantics execute on the generic AST/VM:

```
invoke action → execute effects (assign, transition to, create)
  → stage changes → fire stage-scoped when subscriptions
  → evaluate policy guards → continue or block
```

This does not require implementing all effect types. It requires **one** vertical slice:

- One entity (e.g. Order or Phone Call from the worked examples)
- Actions with `assign`, `transition to`, and `create` effects
- Stage entry/exit effects
- `when property Stage` subscriptions (single-entity, not collection)
- Policy guards on actions and entry/exit

#### 4a. Complete effect lowering (1.5 days)

Current state: `DomainExpressionLoweringPass` handles all 21 expression types. Effect lowering returns `null` for most effect types.

- [ ] **4a.1** Implement `StageTransitionEffect` lowering: lowering produces Syntax AST that calls a runtime `TransitionTo(stageName)` intrinsic. The intrinsic uses the Introspection type system to validate the stage exists.
- [ ] **4a.2** Implement `AssignEffect` lowering for property mutation (assign entity property → compiled expression that evaluates RHS and stores). Already partially working; harden against edge cases.
- [ ] **4a.3** Implement `CreateEntityInstance` lowering: lowering produces Syntax AST for `new` entity construction with initial property values and initial stage.
- [ ] **4a.4** Implement `CompositeEffect` and `ConditionalEffect` lowering — these are already partially done, verify completeness.
- [ ] **4a.5** Implement subscription effect execution: `when property Stage` lowering produces a conditional effect that checks the target entity's current stage and triggers effects.

**Design constraints:**
- No domain-specific VM opcodes (per `docs/decisions/2026-06-08-domain-lowering-boundary.md`)
- Effects lower to generic Syntax nodes — assignments, conditionals, method calls through Introspection
- Stage transitions are observable by default — no publish step needed

#### 4b. Build runtime effect orchestrator (1 day)

- [ ] **4b.1** Create `DomainEffectOrchestrator` that:
  - Accepts an action invocation on an entity instance
  - Evaluates policy guards for the action
  - Executes each effect in sequence (ordered by `EffectOrderingAnalyzer` analysis)
  - After all effects, fires stage-scoped `when` subscriptions
  - Evaluates entry/exit `require` guards during stage transitions
  - Returns completion result or blocked status
- [ ] **4b.2** Wire the orchestrator to use `Interpreter.Compile`/`Execute` for expression evaluation and effect lowering.
- [ ] **4b.3** Ensure the orchestrator composes with the existing `PolicyEvaluator` (VM path) for policy guard evaluation.

#### 4c. Build subscription evaluation engine (1 day)

- [ ] **4c.1** Implement subscription evaluation: when an entity transitions to a stage, evaluate all `StageSubscription` instances across entities that reference the transitioning entity via a relationship path.
- [ ] **4c.2** Implement quantifiers: `when any` (fire when first match), `when all` (fire when all collection elements match), default (fire per-element).
- [ ] **4c.3** Implement scope: subscriptions are active only while the subscriber entity is in the declaring stage. Leaving the stage removes the subscription.

#### 4d. End-to-end integration test (0.5 day)

- [ ] **4d.1** Author a single `.poly` file that exercises the full loop (e.g. a simplified Order entity with: create, submit → confirm → ship, `when payment Received`, policy guards).
- [ ] **4d.2** Parse with Phase 1 parser → evolve → analyze.
- [ ] **4d.3** Execute the action via the orchestrator → verify effects applied → verify stage transition → verify subscription effects fired.
- [ ] **4d.4** Execute via LINQ reference path and verify `AssertVmMatchesLinq` agreement for expression components.
- [ ] **4d.5** Full test run green.

---

## 4. What does NOT happen in this plan

| Thing | Why not | When |
|-------|---------|------|
| Actor keyword in DSL | Phase 2 — actor→entity lowering exists conceptually, not needed for vertical loop | After Phase 4 exit |
| Entity extension (`Name: Parent { }`) | Requires inheritance model in IR | Phase 2+ |
| `parallel` / `schedule at` / `for` | Second-system features — not needed for one vertical slice | Phase 3+ of DSL spec |
| Full effect catalog (invoke, link, unlink, transition-relationship) | Not needed for Order/Phone Call vertical | Phase 2+ of runtime |
| Code generation (REST, gRPC, GraphQL, HATEOAS) | Post-runtime-loop optionality | After Phase 4 |
| Library/import system | Multi-domain composition | After Phase 4 |
| Domain kinds (`service|cli|library`) | Default `service` covers Phase 1 | Phase 2+ |
| Replacing MCP wire format (JSON→DSL) | JSON stays for machines; DSL is human/LLM text format | Never — complementary |
| Integration with Synthesis/AST macros | Separate workstream | Post-Phase-4 |

---

## 5. Risk assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Analyzer gap during event collapse (Phase 1 removes diagnostics users depend on) | Medium | Medium | Keep adapted analyzers producing equivalent diagnostics under new codes; document migration |
| DSL parser scope creep (Phase 2 grows beyond Phase 1 freeze) | High | High | Hard freeze defined in §2a — reject PRs that add Phase 2+ constructs to parser; use NOT YET SUPPORTED errors |
| Round-trip instability (print→parse produces different domain) | Medium | Medium | Structural-equality test in §2c.2; normalize in printer; fail the build if test fails |
| Runtime orchestrator couples to CLR (defeats multi-host Introspection goal) | Medium | High | Orchestrator must use `ITypeDefinition` / `ITypeDefinitionProvider` — no `System.Type` reflection in effect logic |
| Subscription evaluation creates infinite loops (entity A→stage X triggers subscription→entity B→stage Y→triggers subscription→entity A→...) | Medium | Low | CausalityAnalyzer already detects cycles; orchestrator adds depth limit as safety net |
| Event removal breaks existing MCP workflows | Medium | Low | Deprecate event MCP tools one release before removal; add stage-subscription tools during deprecation period |

---

## 6. Estimated timeline

| Phase | Days | Dependencies |
|-------|------|-------------|
| P0: Survey & inventory | 0.5 | None |
| P1: Collapse event IR | 2–3 | P0 |
| P2: DSL freeze & tooling | 3–5 | P1 |
| P3: Shrink mutation surface | 1 | P2 (DSL defines the target surface) |
| P4: Close runtime loop | 3–5 | P1 (stable IR for lowering) + P2 (DSL for authoring) |

**Total estimated: 9.5–14.5 days.**

Order of execution is critical: P1 → P2 → P3 → P4. Each phase depends on the previous phase's IR stability.

---

## 7. Exit criteria (done = all green)

- [ ] No first-class `Event`, `EventSubscription`, `EventCorrelationBinding`, `EventSubscriptionRoutingMode` in the product authoring surface (engine-internal only if needed).
- [ ] No event-related analyzers; stage-subscription analyzers in their place.
- [ ] No event-related `DomainChange` types.
- [ ] Phase 1 DSL grammar frozen; parser produces `DomainChange[]` from `.poly` text.
- [ ] Canonical printer produces stable `.poly` from committed `Domain`.
- [ ] Round-trip tests passing (parse → evolve → analyze → print → parse → evolve → analyze = structural identity).
- [ ] MCP `apply_dsl` tool in both strict and capture modes.
- [ ] MCP tool surface reduced to block-level changes (entity block, relationship, value block, DSL).
- [ ] One end-to-end vertical slice executes on generic AST/VM: action → effects → stage transition → subscription effects → policy guards.
- [ ] All 1175+ existing tests green; new tests for each Phase.
- [ ] `docs/CORE.md` updated to reflect stage-observation model (remove event-centric description).
- [ ] `AGENTS.md` remains unchanged (principles unaffected).
