# MCP Tool Surface — Semantic Gap Analysis & Expansion Plan

**Date:** 2026-07-12  
**Revised:** 2026-07-18 (**Dogfood-2** post-RT → **RT′** + **SA** stage-action next)  
**Status:** Phase 3 + RT **shipped** (dogfood-2 validated); **RT′ / SA** open — not “all gaps closed”  

**Shipped tools (approx.):** ~34+ in `Poly.Mcp/Tools/` (session, query, evolve, policy, constraint, DSL, oracle, suggestions, guide, runtime)  
**Principle:** Thin adapters over `DomainEvolution` / DomainModeling / Interpretation — no new domain semantics; no V2 resurrection; **no event authoring tools** (stage-transition-as-observable).  
**Related:**  
- [`mcp-phase3-oracle-surface.md`](mcp-phase3-oracle-surface.md) — §6c RT done · **§6e SA** · **RT′**  
- Dogfood: [report 1](agent-summaries/dogfood/DOGFOOD-REPORT-20260718.md) · [report 2](agent-summaries/dogfood/DOGFOOD-REPORT-2-20260718.md)  
- [`domainmodeling-next-phase.md`](domainmodeling-next-phase.md) — Phase 2 domain runtime (library)  
- ADR stage-transition-as-observable  

---

## 0. Status as of 2026-07-18 — what shipped vs what’s still missing

### Shipped (plan phases that are no longer gaps)

| Area | Tools / notes |
|------|----------------|
| Session | `create_domain_session`, `list_sessions` |
| Query | `get_domain_overview`, `get_entity_detail`, `get_domain_analysis`, `get_domain_snapshot`, `get_relationships`, `get_domain_suggestions` (A-lite, `02ceee1`) |
| Structure add | `add_entity`, `add_property`, `add_stage`, `add_action`, `add_action_to_stage`, `add_relationship` |
| Batch add | `add_properties`, `add_stages`, `add_actions_to_stages` |
| **Remove / undo** (old plan “Phase 2”) | `remove_entity`, `remove_property`, `remove_stage`, `remove_action`, `remove_action_from_stage`, `remove_relationship`, `remove_policy` |
| Constraints | `add_constraint`, `get_constraints` (**`remove_constraint` still missing**) |
| Policy | `get_policy_expression`, `add_policy` (entity), `evaluate_policy` (multi-property JSON subject) |
| **DSL** | `apply_dsl` (full **replace**), `export_dsl`, `get_dsl_guide` (embedded product guide, `6b0fd63`) |
| **Oracle** | `lower_expression`, `describe_expression`, `describe_domain_element`, `simulate_policy` |
| **Runtime** | `create_instance`, `get_instance`, `list_instances`, `call_action` — **dogfood-2 E2E validated** |

**Honest substitute:** Effects largely via **`apply_dsl`**. **SA:** stage micro-path can silently no-op (empty `AddActionToStage` copies) — fix semantics before more effect tools.

### Still missing / next

| Priority | Bucket | Tools / work | Trigger |
|----------|--------|--------------|---------|
| **P0** | **RT′ honesty/safety** | Analysis→suggestions; CallAction `IsDeleted`; entity policy + subscription docs | Dogfood-2 — phase3 **RT′** |
| **P0** | **SA stage-action semantics** | Fix empty stage action / effect targeting; goldens | Dogfood-2 Score 14 — phase3 **§6e** (DomainModeling core) |
| **P1** | Runtime MCP (RT) | create/call/list instances | ✅ **Shipped** + dogfood-2 |
| **P1** | Parser honesty | `actor` message; optional nav FormatException | RT′.2–.3 |
| **P2** | Visibility / debug (**V1/S1**) | analyze/compare/debug expression | Pull |
| **P2** | Full effect-micro MCP | Per-effect tools | **After SA** only if DSL insufficient |
| **P3** | Library builder hygiene | `AddActionWithEffect` naming | After SA |
| **P3** | Constraint remove | `remove_constraint` | Unexercised |
| **Post–P3** | **L\*** C# → MSIL → **containers** | phase3 **§6d** | Well after Phase 3 + RT |
| **Never** | Event authoring | — | Retired |

### Pick order (agents)

**Dogfood-2:** [DOGFOOD-REPORT-2-20260718](agent-summaries/dogfood/DOGFOOD-REPORT-2-20260718.md)  
**Checklists:** [`mcp-phase3-oracle-surface.md`](mcp-phase3-oracle-surface.md) **§6c RT′** · **§6e SA**

```text
1–6.  V0 / S0 / A / G / dogfood-1 / RT     ← done (RT dogfood-2 validated)
7. ▶  RT′ cheap bundle                      ← next
8. ▶  SA stage-action semantics (§6e)       ← next epic (not full effect-micro)
9.    Full effect-micro / V1 / remove_constraint  ← pull only
10.   Post–P3 L* / containers               ← well after
```

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

**Second loop (post–Phase 3):** the lowered Syntax AST is **an implementation** of domain intent; **C#** (then optional **MSIL/assembly**) are host-consumable projections. Review + goldens improve generation; packaging makes Poly shippable outside the process. See phase3 **§6d**. **Do not schedule this against Phase 3 or RT** — needs RT corpus first, then named need.

**This is the core value of the MCP expansion below.** Not "more tools." A complete feedback loop for any model, at any size — plus, over time, a corpus that improves the platform’s own lowering.

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

### When to build (unified execution plan) — **updated 2026-07-18**

| Phase | Trigger | Tools | Status |
|-------|---------|-------|--------|
| **DSL** | Batch authoring | `apply_dsl`, `export_dsl` | ✅ **Shipped** (replace semantics; dry-run still missing) |
| **Mutate undo** | Agent recovery | remove_* family | ✅ **Shipped** (see §0) |
| **Constraints add** | Property validation | `add_constraint`, `get_constraints` | ✅ **Shipped** |
| **Policy eval multi-prop** | Subject bags | `evaluate_policy` properties JSON | ✅ **Shipped** |
| **V0** | Agent needs pipeline visibility | `lower_expression`, `describe_expression`, `describe_domain_element` | ⬜ **Next pick** — pure, zero risk |
| **S0** | Try-before-commit policies | `simulate_policy` (ad-hoc expr + subject) | ⬜ High value |
| **A0–A2** | Actionable analysis | SuggestionMetadata + `get_domain_suggestions` | ⬜ |
| **V1** | Analysis feedback | `analyze_expression`, `compare_engines` | ⬜ After V0 |
| **S1** | Debug evaluation | `debug_expression` | ⬜ After V0/S0 — VmDebugger |
| **V2** | Metadata / contract diff | `compile_expression`, `diff_expressions` | ⬜ Lower urgency |
| **Effect micro-tools** | Incremental action config without DSL | parameter/effect/transition/assign/create-in/entry-exit wrappers | ⬜ Pull if dogfood refuses DSL |
| **Policy placement** | Stage/action policies via tools | `add_policy_to_stage`, `add_policy_to_action` | ⬜ |
| **Constraint remove** | Symmetric repair | `remove_constraint` | ⬜ |
| **S3** | Before/after confidence | `diff_state` | ⬜ Any time light wrapper |
| **R1** | Reverse-engineering | `apply_dsl_capture` | ⬜ Deferred — `EvolutionStrictness` |
| **Dry-run** | Preview apply | `apply_dsl_dry_run` | ⬜ Optional |
| **Runtime MCP** | In-session CallAction / Link / store | new tools (not model-only session) | ⬜ Named dogfood only |
| **S2** | Effect dry-run | `simulate_effect` | ⬜ Needs instance runtime |
| **Events** | — | event type / publish tools | ❌ **Do not build** — stage transition is the observable |

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
| Add stage | ✅ | Flat stages only (no parent hierarchy) |
| Add action (entity-level) | ✅ | |
| Add action to stage (stage-local) | ✅ | Blank action shell — effects via DSL or missing micro-tools |
| Add relationship | ✅ | |
| Remove entity / property / stage / action / action_from_stage / relationship | ✅ | MR / MR′ shipped |
| Set entity parent | ⬜ | `SetEntityParentChange` exists; pull-only |

### Action parameter & effect tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| Add parameter to action | ⬜ | IR exists; **DSL does not** fully replace params |
| Remove parameter from action | ⬜ | IR exists |
| Add effect to action (generic) | ⬜ | Prefer DSL for batch; micro-tool for incremental |
| Stage-transition / assign / create / create-in effects | ⬜ micro-tool | ✅ via **DSL** `transition` / `assign` / `create` / `create in` |
| Remove effect from action | ⬜ | IR exists |
| Set action result type | ⬜ | IR exists |
| Publish-event effect | ❌ **Skip** | Event authoring path retired |

### Stage effect tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| OnEntry / OnExit effects | ⬜ micro-tool | ✅ via **DSL** `entry` / `exit` |
| Stage subscription add/remove micro-tools | ⬜ | ✅ author via **DSL** `when`; detail via `get_entity_detail` |

### Event tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| All event type / publish / event-subscription tools | ❌ **Skip** | Stage transition is the authorable observable (ADR 2026-07-17) |

### Policy tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| Get policy expression | ✅ | |
| Add policy (entity) | ✅ | Comparisons + composites |
| Evaluate policy | ✅ | Multi-property subject supported |
| Add policy to stage / action | ⬜ | IR exists; remove_policy supports scopes |
| Remove policy | ✅ | Entity / stage / action scope |

### Constraint tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| Add constraint to property | ✅ | `add_constraint` |
| Get constraints | ✅ | |
| Remove constraint from property | ⬜ | Still open (evolutionary repair) |
| Add constraint to type | ⬜ | Pull-only |

### Query tools

| Capability | MCP | Notes |
|-----------|-----|-------|
| Get domain overview | ✅ | |
| Get entity detail | ✅ | Stages, actions, policies, subscriptions, navigations |
| Get domain analysis | ✅ | Text diagnostics / hints — not structured suggestions |
| Get domain snapshot | ✅ | |
| Get relationships | ✅ | |
| List primitives (dedicated) | — | Covered enough by overview / bootstrap |

### Pipeline visibility / simulate / suggestions (still open)

| Capability | MCP | Notes |
|-----------|-----|-------|
| `lower_expression` | ⬜ | **V0** |
| `describe_expression` | ⬜ | **V0** |
| `describe_domain_element` | ⬜ | **V0** — richer than `get_entity_detail` (NL + template) |
| `analyze_expression` | ⬜ | **V1** |
| `compare_engines` | ⬜ | **V1** dual oracle |
| `compile_expression` | ⬜ | **V2** |
| `diff_expressions` | ⬜ | **V2** |
| `simulate_policy` | ⬜ | **S0** ad-hoc expr (vs committed policy only) |
| `debug_expression` | ⬜ | **S1** |
| `simulate_effect` | ⬜ | **S2** needs instance model |
| `diff_state` | ⬜ | **S3** |
| `get_domain_suggestions` | ⬜ | **A2** structured acceptables |
| `apply_dsl_capture` / dry-run | ⬜ | **R1** / optional |
| Runtime: CallAction / Link / store | ⬜ | **Not in original plan** — session is model-only |

---

## 2. Recommended MCP additions (by priority) — **post–2026-07-18**

### Done (do not re-plan)

- Remove family, constraints add/list, multi-property `evaluate_policy`, `apply_dsl` / `export_dsl`, extra query tools — **shipped**.

### Next: Oracle surface (original V0/S0/A*)

| # | Tool / work | Why | Depends |
|---|-------------|-----|---------|
| **V0.1** | `lower_expression` | Deterministic AST visibility | Lowering pass |
| **V0.2** | `describe_expression` | Plain-English + structured guard | Template walk |
| **V0.3** | `describe_domain_element` | Orient agent in model | Detail queries + templates |
| **S0.1** | `simulate_policy` | Ad-hoc expr + subject without committing | PolicyEvaluator |
| **A0–A2** | Suggestion metadata + `get_domain_suggestions` | Acceptable analysis artifacts | Analysis framework |

### Then: depth / repair (pull with pain)

| # | Tool / work | Why |
|---|-------------|-----|
| **V1.*** | `analyze_expression`, `compare_engines` | Type/diag + dual oracle |
| **S1** | `debug_expression` | Step trace |
| **E.*** | Effect/parameter micro-tools | Only if DSL insufficient for incremental agents |
| **P.*** | `add_policy_to_stage` / `add_policy_to_action` | Scoped policy without full DSL rewrite |
| **C.*** | `remove_constraint` | Symmetric constraint repair |
| **R1** | Capture / dry-run apply | Reverse-engineering |

### Runtime MCP (new bucket — not original Phase 1–4)

| # | Tool / work | Why | Trigger |
|---|-------------|-----|---------|
| **RT.1** | Session instance store + create instance | Hold running entities | Dogfood CallAction via MCP |
| **RT.2** | `call_action` / link / inspect store | Close spawn-and-wire **in** MCP | Named agent runtime consumer |
| **RT.3** | `simulate_effect` | Dry-run effects | After RT.1 |

### Do not build

| Tool family | Why |
|-------------|-----|
| Event types / publish / event subscriptions | Product authoring path removed (stage transition is the observable) |

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
  DomainTools.cs     ← current surface (session, query, evolve, policy, constraint, DSL)
  # Prefer adding visibility/simulate tools here or a sibling ExpressionTools.cs when file grows
```

Placement: thin adapters only; lowering/eval/debug live in DomainModeling + Interpretation.

---

## 5. Execution plan

Canonical order: **[§0 pick order](#0-status-as-of-2026-07-18--what-shipped-vs-whats-still-missing)** and the **[unified execution table](#when-to-build-unified-execution-plan--updated-2026-07-18)**.

### Both-and strategy (updated)

1. ✅ **DSL + micro mutate/undo** — dual path shipped  
2. **V0 visibility next** — pure functions, zero risk, agent confidence  
3. **S0 simulate_policy** — verify before commit  
4. **A0–A2 suggestions** — analysis becomes actionable  
5. **Effect micro-tools only if** agents cannot use DSL for incremental effect edits  
6. **Runtime MCP only if** spawn-and-wire must run inside the session (today: model-only)  
7. **Never** resurrect event authoring tools  

**Remaining estimated surface (if all open buckets ship):** ~6 visibility + ~3 simulate/debug + ~1 suggestions + optional effect/policy placement + optional runtime session tools.

---

## 🔗 Related plans (2026-07-13 agent feedback)

Three companion plans were created from agent-driven domain modeling feedback. These address concrete gaps discovered during a ~150-call supply chain modeling session:

| Plan | Focus | Overlaps with this doc |
|------|-------|----------------------|
| [`mcp-mutation-safety.md`](../../mcp-mutation-safety.md) | Parallel call safety, idempotency, rollback diagnostics, stage ordering | `diff_state` tool (covered here); concurrency model (new) |
| [`mcp-batch-snapshot-efficiency.md`](../../mcp-batch-snapshot-efficiency.md) | Bulk/plural endpoints (`add_properties`, `add_stages`, `add_actions_to_stages`), `get_domain_snapshot` | DSL batch-apply path (this doc); plural endpoints are a complementary approach |
| [`mcp-domain-inspection-completeness.md`](../../mcp-domain-inspection-completeness.md) | `get_relationships`, `add_constraint`/`get_constraints`, constraint analysis integration | Relationship and constraint tools are new; not covered here |
