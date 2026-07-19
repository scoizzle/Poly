# Poly.Mcp — MCP Server for Poly Domain Modeling

## Tool Surface

Tools live in `Poly.Mcp/Tools/` and use only `Poly.DomainModeling` types (no `Poly.Data.Modeling`).
**40 tools** registered via `Program.cs` (`SessionTool`, `QueryTool`, `EvolveTool`, `PolicyTool`, `DslTool`, `OracleTool`, `RuntimeTool`).

### Session

| Tool | Class | Purpose |
|------|-------|---------|
| `create_domain_session` | `SessionTool` | Creates a bootstrapped domain session with built-in primitive types |
| `list_sessions` | `SessionTool` | Lists active domain sessions |

### Query / inspect

| Tool | Class | Purpose |
|------|-------|---------|
| `get_domain_overview` | `QueryTool` | Returns domain overview with entity/primitive/relationship counts |
| `get_entity_detail` | `QueryTool` | Returns entity properties, stages, actions, policies, navigations, subscriptions |
| `get_domain_analysis` | `QueryTool` | Returns analysis diagnostics (errors, warnings, info, hintCount) |
| `get_domain_suggestions` | `QueryTool` | Authoring suggestions (advisory DMAS001 hints) |
| `get_domain_snapshot` | `QueryTool` | Full model dump: entities, relationships, analysis |
| `get_relationships` | `QueryTool` | Lists relationships; optional entity filter |
| `get_constraints` | `EvolveTool` | Lists constraints on an entity's properties |

### Evolve (analysis-gated mutation)

| Tool | Class | Purpose |
|------|-------|---------|
| `add_entity` | `EvolveTool` | Adds a new entity type |
| `add_property` | `EvolveTool` | Adds a property to an existing entity |
| `add_stage` | `EvolveTool` | Adds a lifecycle stage to an entity |
| `add_action` | `EvolveTool` | Adds an entity-level action |
| `add_action_to_stage` | `EvolveTool` | Places/copies an action onto a stage (documents fallthrough) |
| `add_relationship` | `EvolveTool` | Adds a relationship between entities |
| `add_constraint` | `EvolveTool` | Adds Range/Required/Length/Pattern/Unique to a property |
| `add_properties` | `EvolveTool` | Atomic batch of properties |
| `add_stages` | `EvolveTool` | Atomic batch of stages |
| `add_actions_to_stages` | `EvolveTool` | Atomic batch of stage action placements |
| `remove_entity` | `EvolveTool` | Removes an entity (analysis gate on dependents) |
| `remove_property` | `EvolveTool` | Removes a property |
| `remove_stage` | `EvolveTool` | Removes a stage and its children |
| `remove_action` | `EvolveTool` | Removes an entity-level action |
| `remove_action_from_stage` | `EvolveTool` | Removes a stage-scoped action |
| `remove_policy` | `EvolveTool` | Removes a policy (entity/stage/action scope) |
| `remove_relationship` | `EvolveTool` | Removes a relationship by name |

### Policy

| Tool | Class | Purpose |
|------|-------|---------|
| `get_policy_expression` | `PolicyTool` | Inspect-only guard expression text |
| `add_policy` | `PolicyTool` | Adds entity-level policy from JSON expression |
| `evaluate_policy` | `PolicyTool` | VM-evaluates a named policy against a **local** subject bag |

### DSL

| Tool | Class | Purpose |
|------|-------|---------|
| `apply_dsl` | `DslTool` | Applies a `.poly` DSL document, **replacing** the session domain |
| `export_dsl` | `DslTool` | Exports the current session domain as `.poly` DSL text |
| `get_dsl_guide` | `DslTool` | Product-true Phase 1a/1b syntax guide (**embedded resource only** — pack must include `Docs/poly-dsl-agent-guide.md` as EmbeddedResource; no filesystem fallback) |

### Oracle

| Tool | Class | Purpose |
|------|-------|---------|
| `lower_expression` | `OracleTool` | Lowers a JSON policy expression to Syntax AST (no session) |
| `describe_expression` | `OracleTool` | Structured + plain-English expression breakdown (no session) |
| `describe_domain_element` | `OracleTool` | Describes entity/stage/action/policy/relationship |
| `simulate_policy` | `OracleTool` | VM-evaluates a JSON expression against a subject bag (no session) |

### Runtime

| Tool | Class | Purpose |
|------|-------|---------|
| `create_instance` | `RuntimeTool` | Creates a runtime instance and registers it in the session store |
| `get_instance` | `RuntimeTool` | Snapshot: stage, properties, deletion status, child count |
| `list_instances` | `RuntimeTool` | Lists runtime instances (skips deleted); optional entity filter |
| `invoke_action` | `RuntimeTool` | Invokes an action: guards → effects → stage transition → subscription fan-out |

## Dual Authoring Path

Poly.Mcp supports two complementary ways to build a domain model, each suited to different workflows.

### Batch Path (`apply_dsl`)

Before authoring a large domain, call **`get_dsl_guide`** to retrieve the product-true syntax guide.
This avoids inventing unsupported lab constructs.

Write the full domain in a single `.poly` DSL document and apply it in one shot. The session's domain is **replaced** entirely — not merged incrementally.

```
domain Orders

Customer: entity {
  Name: Text required
  Email: Text required unique
  Places: many Order
}

Order: entity {
  Total: Number
  Draft: stage {
    Submit: action { transition to Active }
  }
  Active: stage {}
}
```

**Use when**: bootstrapping from scratch, iterating in a text editor, or recreating a known state. Parse errors produce line/col diagnostics.

### Incremental Path (micro-tools)

Use `add_entity`, `add_property`, `add_stage`, `add_action`, `add_relationship`, etc. to build the model one piece at a time. Each tool call is a single `DomainChange` that goes through the full analysis pipeline, so errors are caught immediately.

**Use when**: exploring a model interactively, responding to user prompts in a chat UI, or programmatic construction where each step needs validation.

### Choosing Between Them

| Scenario | Preferred Path |
|----------|---------------|
| Starting a new model from a known definition | Batch (`apply_dsl`) |
| Iterating on a DSL file in an editor | Batch (`apply_dsl`) |
| Reproducing a bug or known state | Batch (`apply_dsl`) |
| Interactive exploration | Incremental (micro-tools) |
| AI agent building a model step by step | Incremental (micro-tools) |
| Round-tripping (export → edit → re-apply) | Batch (`export_dsl` → `apply_dsl`) |

Both paths converge to the same internal representation and produce identical models.

## Tool Honesty Invariant

Every MCP tool's **Name + Description + Success** must match actual behavior:

| If the tool… | Then… |
|--------------|--------|
| Name/Description says evaluate / VM / true-false | Must call the VM path and return `data.result: bool` |
| Only inspects metadata | Must be named/described as inspect/get/describe — **never** "evaluates via VM" |
| Evaluation fails | `Success: false` (or explicit error), not success without a bool |

**Current policy tools:** `get_policy_expression` (inspect-only, no VM), `add_policy` (mutation, no eval), `evaluate_policy` (VM eval, returns bool). All three satisfy the invariant.

**DSL tools:** `apply_dsl` (parses .poly text → evolves empty domain → analysis gate → replaces session domain; revision+1; clears runtime instances; explicit HONESTY NOTES: action `when Stage` is not a separate runtime gate, subscriptions need RuntimeTool instances to fan out), `export_dsl` (printer round-trip, no side effects), `get_dsl_guide` (embedded product guide).

## Runtime Tools — Exercise Domain Lifecycle

The **RuntimeTool** family closes the final feedback loop: agents can create instances, inspect state, and execute actions — all within the MCP session.

### Create → Call → Observe

```text
1. apply_dsl / micro-tools  →  model in session
2. create_instance          →  instanceId + initial snapshot
3. invoke_action              →  effects execute, stage transitions, subscriptions fire
4. get_instance             →  observe new stage + modified properties
5. list_instances           →  enumerate all instances (optionally by entity)
```

### Instance lifecycle

- Instances are **session-scoped** — each session has its own `DomainInstanceStore`.
- The **first defined stage** is the initial stage (if stages exist).
- `invoke_action` resolves from the **current stage** first, then entity-level actions.
- Guard policies (action-level, stage-level, entity-level) are evaluated before effects.
- On **stage transition**: OnExit → set new stage → OnEntry → notify store subscribers.
- Stage subscription fan-out happens automatically for linked subscriber instances.
  - Subscriptions fire when the relationship **TARGET** entity enters a matching stage (not the source).
  - Example: `when orders Active { ... }` on Customer fires when a linked Order enters its Active stage.
- Deleted instances (`DeleteEntityInstance` effect) are marked `isDeleted: true`.
- Calling actions on deleted instances is refused (returns error).

### Honesty

- `create_instance` **writes** session instance state (creates + registers an instance).
- `get_instance` / `list_instances` are **inspect** tools — they read state, no execution.
- `invoke_action` uses the **same `DomainEntityInstance.InvokeAction` path** as the core library — VM for assign/conditionals, direct execution for transition/create/delete/link.
- Successful `apply_dsl` / evolve replaces the domain root and **clears** prior runtime instances (they held the previous entity graph).
- Related-policy expressions (`Rel.Prop`, `Rel exists`, `Rel where`) are **authorable** but not RT-evaluated by `evaluate_policy` / `simulate_policy` (local property bag only).
