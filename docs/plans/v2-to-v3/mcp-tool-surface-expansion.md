# MCP Tool Surface — Semantic Gap Analysis & Expansion Plan

**Date:** 2026-07-12  
**Status:** Proposal — post-M2 capability expansion  
**Current tools:** 15 (V3-only)  
**Estimated addition:** ~8–12 tools *or* DSL-first  
**Principle:** Thin adapters over `DomainEvolution` — no new domain semantics; no V2 resurrection.

---

## 🧠 The neurosymbolic loop (why this matters)

MCP tool surface isn't an end in itself. It enables a feedback loop that decouples **model capability** from **output correctness**:

```text
┌─────────────────────────────────────────────────────────────┐
│  Agent proposes                                            │
│    → lower (deterministic)                                 │
│    → analyze (deterministic)                               │
│    → describe_domain_element (deterministic template)      │
│    → simulate_policy (deterministic VM)                    │
│    → debug_expression (deterministic, step through)        │
│  Agent corrects based on factual feedback ←─────────────  │
│    → commits via add_policy / apply_dsl                    │
│    → VM evaluates → correct result                         │
└─────────────────────────────────────────────────────────────┘
```

Every step the agent takes is checked by a **deterministic, model-agnostic oracle** — lowering, analysis, VM execution, the debugger. The agent never needs to be right the first time; it only needs to be able to *inspect and correct* based on factual feedback.

This means the platform works identically regardless of which model drives the agent:
- **GPT-5**, **Claude**, **DeepSeek**, or a **local 7B model** — all produce correct Poly artifacts, because correctness comes from the MCP feedback loop, not from parametric knowledge of the format.
- The platform acts as a **correctness oracle**: the model proposes, the VM disposes.
- The MCP tool surface *is* the interface to that oracle. Every visibility tool (`lower_expression`, `describe_expression`, `simulate_policy`, `debug_expression`) closes a loop the agent would otherwise fill with hallucination.

**This is the core value of the MCP expansion below.** Not "more tools." A complete feedback loop for any model, at any size.

---

## ⚠️ Key observation: DSL as a complementary path

A domain-specific language (DSL) designed for domain mutations would be a **complementary medium** — not a replacement for individual MCP tools.

| Aspect | Individual MCP tools | DSL batch string |
|--------|---------------------|------------------|
| Discovery | Tool list *is* the API — agent sees all capabilities | Must be fetched or guessed |
| Token cost per mutation | Higher (tool name + args + overhead) | Lower (compact syntax) |
| Atomicity per batch | Tool-level only | Batch-level (single evolve + analysis gate) |
| Agent orchestration | Must chain tools sequentially | Single parse + apply |
| Error recovery | Dedicated per-tool affordances | Rollback entire batch |
| Best for | Simple ops, discovery, "what can I do?" | Complex multi-step mutations |

**Both paths should exist.** The DSL is an optimization for batch operations; the individual tools are the discoverable surface. The DSL doesn't eliminate the need for the tool inventory — it *consumes* the same `DomainChange` types underneath.

---

## 🧪 Bonus: Simulation & debug surface

Agents shouldn't have to commit blindly. The MCP can expose a **simulation sandbox** using the Interpretation layer + VmDebugger, letting agents verify expressions and step through state transitions before applying changes.

| Tool | What it does | Infrastructure | Status |
|------|-------------|----------------|--------|
| `simulate_policy` | Accept a policy expression contract + subject values → compile → VM evaluate → return bool | `PolicyExpressionParser` + `PolicyEvaluator` + arbitrary subject builder | ⬜ New — extends `evaluate_policy` to arbitrary subjects, not just Age |
| `debug_expression` | Accept an expression + subject → compile → return `VmProgram` + step through with VmDebugger, returning each statement's node and locals | `VmDebugger` (already built, works, flake fixed) | ⬜ New — wraps step-over in stateless per-call interface |
| `simulate_effect` | Accept an effect type + parameters + mock entity values → apply via lowering + VM → return resulting state delta | Effect system + `DomainExpressionLoweringPass` + VM | ⬜ New — harder, needs runtime subject model (Slice 4 prerequisite) |
| `diff_state` | Before/after comparison of an entity's properties after an effect or evolve batch | `EvolutionResult` already provides old/new root | ⬜ New — light wrapper over `DiffDomainRevision` pattern |

### How `debug_expression` would work

```
Agent provides:
  { "expression": {"property":"Age","op":">=","value":18},
    "subject": {"Age": 25} }

MCP returns:
  {
    "steps": [
      {"node":"Member(Age)", "locals":{"Age":25}, "value":25},
      {"node":"Constant(18)", "locals":{"Age":25}, "value":18},
      {"node":"GreaterThanOrEqual", "result": true}
    ],
    "finalResult": true
  }
```

This uses the existing `VmDebugger` — compile the lowered expression, attach a `DebugHook` that captures per-node values, run, return the trace. No new VM infrastructure needed.

---

## 📝 Capture mode — model imperfect systems

Current `DomainEvolution.Apply` has one strictness: **errors → rollback**. This is correct for forward-engineering, but wrong for reverse-engineering a real system that contains contradictions.

A **Capture mode** would commit the domain model even when analysis finds errors. Errors become diagnostics on the committed domain, not gating conditions:

```csharp
// Forward-engineering (default) — errors → rollback
result = evolution.Apply(changes);

// Reverse-engineering (Capture) — errors committed as diagnostics
result = evolution.Apply(changes, strictness: EvolutionStrictness.Capture);
```

### Why this matters for reverse-engineering legacy systems

| Scenario | Strict mode | Capture mode |
|----------|------------|--------------|
| Order transitions: Pending→Shipped→Refunded | ✅ Valid, no errors | Identical |
| Order transitions: Shipped→Pending (legacy bug) | ❌ Rolled back — rejected | ✅ Committed with diagnostic: "Stage 'Shipped' cannot transition to 'Pending'" |
| Two stages with same name | ❌ Structural failure | ✅ Committed with structural diagnostic |
| Policy references missing property | ❌ Rolled back | ✅ Committed with diagnostic: "Policy 'X' references property 'Y' which doesn't exist" |

An agent reverse-engineering a legacy system can:

1. `apply_dsl` in Capture mode — ingest the full system, contradictions and all
2. `describe_domain_element` — inspect the captured model
3. Iteratively refactor toward a valid model, or document known contradictions

### MCP surface

| Tool | What it does | Infrastructure |
|------|-------------|----------------|
| `apply_dsl_capture` | Same as `apply_dsl` but Capture mode — commits even with errors | New `EvolutionStrictness` parameter on `Apply` |

### When to build

**Deferred** — needed when the first reverse-engineering scenario arrives. The core change is small (add `strictness` param to `DomainEvolution.Apply`) but the tooling surface depends on `apply_dsl` existing first.

---

## 🧩 Actionable analysis suggestions

Today, the domain model already has 23 analysis passes (`Poly/DomainModeling/Analysis/`). Some already emit hints as text diagnostics (`AuthoringSuggestionGenerator`, `IdempotencySafetyAnalyzer`). The gap: suggestions are advisory text, not actionable artifacts an agent can apply.

### Current state

```csharp
// advisory text only — agent must re-derive the solution
context.ReportHint(entity, "Many actions; consider stages.");
```

### Future state

```csharp
// structured suggestion with an artifact the agent can inspect and apply
context.ReportSuggestion(entity, new Policy("Guard_AlreadyProcessed",
    DomainExpression.Equal(
        DomainExpression.Property("IsProcessed"),
        DomainExpression.Literal(false))));
```

When analysis produces a structured `SuggestionMetadata`, the MCP tool can return it:

```json
{
  "suggestions": [
    {
      "kind": "policy",
      "target": "ProcessPayment",
      "policy": {
        "name": "Guard_AlreadyProcessed",
        "expression": {"property": "IsProcessed", "op": "==", "value": false}
      },
      "rationale": "Action 'ProcessPayment' checks for duplicates. Adding this guard prevents processing the same payment twice.",
      "acceptTool": "add_policy"
    }
  ]
}
```

The agent's workflow becomes the full loop:

```
analyze domain
  → ("add Guard_AlreadyProcessed policy?")
  → describe_expression("IsProcessed == false")
  → simulate_policy({"IsProcessed": true}) → false (correct)
  → accept → add_policy(...)
```

### What already exists

| Piece | Location | Status |
|-------|----------|--------|
| `INodeAnalyzer` pass contract | `Syntax/Analysis/` | ✅ Production |
| `AuthoringSuggestionGenerator` | `DomainModeling/Analysis/` | ✅ Production (text hints) |
| `IdempotencySafetyAnalyzer` | `DomainModeling/Analysis/` | ✅ Production (text hints) |
| `DomainModelAnalyzer` pipeline | `DomainModeling/Analysis/DomainModelAnalyzer.cs` | ✅ Registered |
| `AnalysisContext.ReportHint` | `Syntax/Analysis/AnalysisContext.cs` | ✅ Emits `Suggestion` severity diagnostic |
| Domain suggestion metadata | 🟡 **New** — `SuggestionMetadata` record | ⬜ Needed |
| MCP `get_domain_analysis` | `Poly.Mcp` | ✅ Already returns diagnostics |

### What's needed

1. A `SuggestionMetadata : IAnalysisMetadata` record holding the structured suggestion (kind, payload DomainExpression/Policy, rationale)
2. `AnalysisContext.ReportSuggestion<T>(node, suggestion, rationale)` extension method
3. An `AffordanceSuggestionAnalyzer` pass that walks the domain and generates suggestions (can start with porting the existing hint generators)
4. MCP `get_domain_suggestions` tool that returns only suggestion-severity diagnostics with structured metadata
5. Agent can accept via existing `add_policy` / `add_constraint_to_property` tools — suggestions reference the same mutation surface

### When to build

| Phase | What | Why |
|-------|------|-----|
| **S0** | `SuggestionMetadata` + `ReportSuggestion` | Core infrastructure, small diff |
| **S1** | `AffordanceSuggestionAnalyzer` + port existing hint generators | Concrete value — starts producing actionable suggestions |
| **S2** | MCP `get_domain_suggestions` | Agent surface — returns structured, inspectable, actionable suggestions |
| **S3** | Accept workflow (`describe_expression` → `simulate_policy` → `add_policy`) | Already covered by V0/S0 tools — suggestions just feed into them |

## 🔍 Pipeline visibility tools

Agents currently work blind: they submit an expression contract, it either succeeds or fails, but they never see what's *inside* the pipeline. These tools expose every layer of the `lower → analyze → compile → execute` path as inspectable outputs.

| Tool | What it reveals | Infrastructure | Risk of hallucination |
|------|----------------|----------------|----------------------|
| `lower_expression` | Accept `PolicyExpressionContract` → return lowered Syntax AST tree (structured JSON + string) | `DomainExpressionLoweringPass` — pure function | **Zero** — deterministic lowering |
| `analyze_expression` | Accept lowered AST → run analysis → return diagnostics (type resolutions, warnings, errors) | `Interpreter.Analyze` + `AnalysisResult.Diagnostics` | **Zero** — pure analysis pass |
| `compile_expression` | Accept lowered AST → return program metadata (register count, node count, debug info, root value kind) | `Interpreter.Compile` + `VmProgram` metadata | Low — compile-time metadata |
| `describe_expression` | Accept contract → return structured form **and** plain English | Expression tree walk + template | Medium — NL generation, but the structured data grounds it |
| `compare_engines` | Accept contract + subject values → return VM result and LINQ result, warn on divergence | `EvaluateWithDualOracle` — already exists, tested | **Zero** — actual execution |
| `describe_domain_element` | Accept entity/stage/action/policy name → return structured breakdown **and** plain-English description of the whole element, including nested policy expressions | Template generator per element type (drills into `describe_expression` for policies) | Low — deterministic templates; expression parts use symbol-to-text mapping |
| `diff_expressions` | Accept two contracts → return structural diff (added/removed/changed clauses) | Tree diff on `PolicyExpressionContract` | Low — structural comparison |

### `describe_domain_element` — domain narrator

Accepts any named domain element (entity, stage, action, policy, relationship) and returns a **structured breakdown** plus a **plain-English description** the agent can rely on without parsing raw AST.

```json
// Input: describe entity "Order"
{
  "entity": "Order"
}

// Output
{
  "structured": {
    "type": "entity",
    "name": "Order",
    "properties": [
      {"name": "Status", "type": "Text", "constraints": []},
      {"name": "Total", "type": "Number", "constraints": []}
    ],
    "stages": [
      {"name": "Draft", "actionCount": 1, "policyCount": 0,
       "actions": ["Submit"]},
      {"name": "Submitted", "actionCount": 0, "policyCount": 1,
       "policies": ["LargeActive"]}
    ],
    "entityActions": ["Submit", "Cancel"],
    "policies": [{"name": "LargeActive", "expression": "Total > 100 AND Status == 'Active'"}]
  },
  "description": "Order entity with 2 properties (Status, Total), 2 stages (Draft, Submitted), and 3 actions (Submit, Cancel, Submit on Draft). Has 1 policy: LargeActive (requires Total > 100 and Status is Active)."
}
```

The template generator handles each element type:

| Element | Description template |
|---------|---------------------|
| Entity | `"{name} entity with {N} properties ({names}), {N} stages ({names}), and {N} actions ({names}). Has {N} policies: {summaries}."` |
| Stage | `"{name} stage on {entity} has {N} actions ({names}), {N} policies ({summaries})."` |
| Action | `"{name} on {entity}: returns {resultType}. Parameters: {params}. Effects: {effectSummaries}."` |
| Policy | `"{policyName} on {entity}: {structured} → {plainEnglish}."` |
| Relationship | `"{name}: {source} → {target} ({cardinality}, {ownership})."` |

The policy's `structured` and `plainEnglish` fields use the same template generator from `describe_expression` — they're embedded recursively.

`describe_expression` and `describe_domain_element` are complementary: the former drills into a single expression (what does this guard say?), the latter orients the agent in the domain model (what is this entity?). They share the same expression-to-plain-English template generator: `describe_domain_element` embeds it for policies, while `describe_expression` exposes it directly for any contract.

### Agent workflow with visibility

```
craft expression
  → lower_expression (see AST)        ← "is this what I intended?"
  → analyze_expression (check types)  ← "did the resolver understand it?"
  → describe_domain_element           ← "does the entity and its policy make sense?"
  → simulate_policy (test values)     ← "does it evaluate correctly?"
  → debug_expression (step if wrong)  ← "where does the logic go wrong?"
  → add_policy (commit)               ← "I'm confident"
```

### When to build

| Tool | Priority | Why |
|------|----------|-----|
| `lower_expression` | **Highest** | Pure function, zero risk, immediately useful for agent confidence |
| `describe_expression` | **Highest** | Simple template generator, eliminates guessing about a single guard |
| `describe_domain_element` | **Highest** | Orient the agent in the domain model — what is this entity/stage/action? |
| `analyze_expression` | High | Reuses existing `Interpreter.Analyze` |
| `compare_engines` | High | Already exists as `EvaluateWithDualOracle` |
| `compile_expression` | Medium | Metadata is already in `VmProgram` |
| `diff_expressions` | Medium | Structural diff on contracts |

### When to build (unified execution plan)

| Phase | Trigger | Tools | Notes |
|-------|---------|-------|-------|
| **V0** | Agent needs pipeline visibility | `lower_expression`, `describe_expression`, `describe_domain_element` | **Highest priority** — pure functions, zero risk |
| **S0** | Agent needs to test expressions | `simulate_policy` | High value, low cost — follows `evaluate_policy` pattern |
| **V1** | Agent needs analysis feedback | `analyze_expression`, `compare_engines` | Reuses existing infrastructure |
| **S1** | Agent needs to debug evaluation | `debug_expression` | After V0/V1 — reuses VmDebugger |
| **A0** | Suggestions infrastructure | `SuggestionMetadata`, `ReportSuggestion` | Small diff, core enabler for actionable analysis |
| **A1** | Actionable suggestion pass | `AffordanceSuggestionAnalyzer` | Port existing text-hint generators to structured output |
| **A2** | Suggestions in MCP | `get_domain_suggestions` | Returns inspectable, actionable suggestions |
| **V2** | Agent needs metadata | `compile_expression`, `diff_expressions` | Lower urgency |
| Phase 1 | Agent can't configure actions | `add_parameter_to_action`, `add_effect_to_action`, `add_stage_transition_effect`, `add_publish_event_effect` | Unblocks agents — make actions do something |
| Phase 2 | Agent needs undo | `remove_entity`, `remove_property`, `remove_action`, `remove_stage`, `remove_policy` | After Phase 1 |
| Phase 3 | Policy depth | `add_policy_to_stage`, JSON subject eval, `add_constraint_to_property` | After Phase 2 |
| **S2** | Runtime subject model exists | `simulate_effect` | Blocked on Slice 4 |
| **S3** | Agent needs before/after confidence | `diff_state` | Light wrapper, any time |
| **R1** | Reverse-engineering legacy systems | `apply_dsl_capture` | **Deferred** — depends on `apply_dsl`; needs `EvolutionStrictness` |
| **DSL** | DSL design is stable | `apply_dsl` + `apply_dsl_dry_run` | Optional — parallel to other phases |
| Phase 4 | Events (pull) | `add_event_type`, `add_event_to_entity` | Only when demo needs it |

---

## 1. Gap inventory

### Legend

| Mark | Meaning |
|------|---------|
| ✅ | Exists in MCP |
| ⬜ | Exists in `EvolutionBuilder` but **no MCP tool** |
| ❌ | V2-only, deleted with no V3 replacement |
| **Skip** | Not yet needed (post-M2 pull) |

### Structure tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| Create session | ✅ | |
| Add entity | ✅ | |
| Add property | ✅ | |
| Add stage | ✅ | |
| Add action (entity-level) | ✅ | |
| Add action to stage (stage-local) | ✅ | Creates blank action — **semantic gap** |
| Add relationship | ✅ | |
| Remove entity | ⬜ | `RemoveEntityChange` exists |
| Remove property | ⬜ | `RemovePropertyFromEntityChange` exists |
| Remove stage | ⬜ | `RemoveStageChange` exists |
| Remove action (entity) | ⬜ | `RemoveActionChange` exists |
| Remove action (stage) | ⬜ | `RemoveActionFromStageChange` exists |
| Remove relationship | ⬜ | `RemoveRelationshipChange` exists |
| Set entity parent | ⬜ | `SetEntityParentChange` exists |

### Action parameter & effect tools

This is the **biggest gap**: actions are currently inert shells.

| Capability | MCP | Notes |
|-----------|-----|-------|
| Add parameter to action | ⬜ | `AddParameterToActionChange` exists |
| Remove parameter from action | ⬜ | `RemoveParameterFromActionChange` exists |
| Add effect to action | ⬜ | `AddEffectToActionChange` exists |
| Add stage-transition effect | ⬜ | `AddStageTransitionEffect` exists |
| Add assign effect | ⬜ | `AssignEffect` + `AddEffectToActionChange` exists |
| Add publish-event effect | ⬜ | `PublishEventEffect` + `AddEffectToActionChange` exists |
| Add create-entity effect | ⬜ | `CreateEntityInstance` + `AddEffectToActionChange` exists |
| Remove effect from action | ⬜ | `RemoveEffectFromActionChange` exists |
| Set action result type | ⬜ | `SetActionResultChange` exists |

### Stage effect tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| Add OnEntry effect to stage | ⬜ | Exists |
| Add OnExit effect to stage | ⬜ | Exists |
| Remove OnEntry/OnExit effect | ⬜ | Exists |

### Event tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| Add event type | ⬜ | `AddEventChange` exists |
| Remove event type | ⬜ | `RemoveEventChange` exists |
| Add event ref to entity | ⬜ | `AddEventReferenceToEntityChange` exists |
| Remove event ref from entity | ⬜ | Exists |
| Add event subscription | ⬜ | Exists |
| Remove event subscription | ⬜ | Exists |

### Policy tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| Get policy expression | ✅ | |
| Add policy (simple expr) | ✅ | Supports property comparisons + composites |
| Evaluate policy (Age) | ✅ | Single subject property |
| Add policy to stage | ⬜ | `AddPolicyToStageChange` exists |
| Add policy to action | ⬜ | `AddPolicyToActionChange` exists |
| Remove policy | ⬜ | `RemovePolicyFromEntityChange` exists |

### Constraint tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| Add constraint to property | ⬜ | `AddConstraintToPropertyChange` exists |
| Remove constraint from property | ⬜ | Exists |
| Add constraint to type | ⬜ | Exists |

### Query tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| Get domain overview | ✅ | |
| Get entity detail | ✅ | |
| Get domain analysis | ✅ | |
| List primitives | ❌ | V2 deleted; no V3 query for this |
| List entities | ❌ | V2 deleted; `get_domain_overview` has counts but not full list |
| List relationships | ❌ | Same |

---

## 2. Recommended MCP additions (by priority)

### Phase 1 — Make actions usable (unblocks agents)

These fill the worst gap: actions exist but can't be configured.

| # | Tool | EvolutionBuilder method | Why |
|---|------|------------------------|-----|
| **P1.1** | `add_parameter_to_action` | `AddParameterToAction(entity, action, param)` | Actions need parameters |
| **P1.2** | `add_effect_to_action` | `AddEffectToAction(entity, action, effect)` | Actions need behavior |
| **P1.3** | `add_stage_transition_effect` | `AddStageTransitionEffect(entity, action, targetStage)` | Most common effect — moves entity between stages |
| **P1.4** | `add_publish_event_effect` | `AddPublishEventEffect(entity, action, eventName)` | Second most common effect |

**Pattern:** Each is a thin wrapper — call `Evolve()`, apply the change, return `V3Response`. Follow the existing `V3EvolveTool` pattern. Effect contracts are flat args (target stage name, event name, property bindings as JSON string for composites).

### Phase 2 — Add remove/undo (agent recovery)

| # | Tool | EvolutionBuilder method | Why |
|---|------|------------------------|-----|
| **P2.1** | `remove_entity` | `RemoveEntityChange` | Agent can delete mistakes |
| **P2.2** | `remove_property` | `RemovePropertyFromEntityChange` | Same |
| **P2.3** | `remove_action` | `RemoveActionChange` + `RemoveActionFromStageChange` | Same |
| **P2.4** | `remove_stage` | `RemoveStageChange` | Same |
| **P2.5** | `remove_policy` | `RemovePolicyFromEntityChange` | Same |

### Phase 3 — Policy placement + evaluation depth (agent confidence)

| # | Tool | Why |
|---|------|-----|
| **P3.1** | `add_policy_to_stage` | Policies on stages not just entities |
| **P3.2** | `evaluate_policy` JSON subject body | Accept full `StrictBag`-style JSON instead of only Age |
| **P3.3** | `add_constraint_to_property` | Validation constraints via MCP |

### Phase 4 — Events (niche, pull-only)

| # | Tool | Why |
|---|------|-----|
| **P4.1** | `add_event_type` | Demos need events |
| **P4.2** | `add_event_to_entity` | Wire events to entities |

---

## 3. Design rules for new tools

1. **One concern per tool.** Do not create a composite "add action with params" tool. Compose via multiple calls.
2. **Flat args preferred.** JSON strings only for composite structures (property bindings, sub-expressions). Use `PolicyExpressionContract` pattern.
3. **Fingerprint-free.** New tools use `EvolutionResult.Succeeded/FailureSummary` directly — fingerprint is only needed for the original `Evolve` shared helper.
4. **Fail-loud.** `RequireUpdate` already handles missing targets. New tools must not swallow errors.
5. **Affordances.** Every tool returns success and failure affordances that make the next step obvious.
6. **Honest descriptions.** The tool description must match what it does (S0.2 rule).

---

## 4. File structure

```
Poly.Mcp/Tools/
  V3DomainTools.cs     ← existing (15 tools) — add Phase 1 here as V3EvolveTool methods
  V3EffectTools.cs     ← new file — Phase 1.2–1.4 effect-specific tools
```

Or keep all in `V3DomainTools.cs` until the file grows past ~1000 lines. Prefer a single file for now (< 1000 lines after additions).

---

## 5. Execution plan

See the **[unified execution table](#when-to-build-unified-execution-plan)** above. The earlier table is canonical — this section is retained as a placeholder for schedule notes.

### Both-and strategy

1. **Build V0–V1 visibility tools first** — pure functions, zero risk, give agents confidence
2. **Build S0–S1 simulation tools** — agents can verify before committing (high value, low cost)
3. **Build Phase 1–4 MCP tools** — discoverable per-op surface for agents
4. **Build DSL + `apply_dsl` in parallel** — batch optimization for complex mutations
5. All tools consume the same `DomainChange[]`, `PolicyExpressionContract`, and analysis infrastructure — no divergence

**Total with all paths:** ~14 individual MCP tools + 2 DSL tools + 4 simulation/debug tools + 6 pipeline visibility tools + `evaluate_policy`.

---

## 🔗 Related plans (2026-07-13 agent feedback)

Three companion plans were created from agent-driven domain modeling feedback. These address concrete gaps discovered during a ~150-call supply chain modeling session:

| Plan | Focus | Overlaps with this doc |
|------|-------|----------------------|
| [`mcp-mutation-safety.md`](../../mcp-mutation-safety.md) | Parallel call safety, idempotency, rollback diagnostics, stage ordering | `diff_state` tool (covered here); concurrency model (new) |
| [`mcp-batch-snapshot-efficiency.md`](../../mcp-batch-snapshot-efficiency.md) | Bulk/plural endpoints (`add_properties`, `add_stages`, `add_actions_to_stages`), `get_domain_snapshot` | DSL batch-apply path (this doc); plural endpoints are a complementary approach |
| [`mcp-domain-inspection-completeness.md`](../../mcp-domain-inspection-completeness.md) | `get_relationships`, `add_constraint`/`get_constraints`, constraint analysis integration | Relationship and constraint tools are new; not covered here |
