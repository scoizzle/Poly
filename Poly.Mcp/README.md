# Poly.Mcp — MCP Server for Poly Domain Modeling

## Tool Surface

Tools live in `Poly.Mcp/Tools/` and use only `Poly.DomainModeling` types (no `Poly.Data.Modeling`).

| Tool | Class | Purpose |
|------|-------|---------|
| `create_domain_session` | `SessionTool` | Creates a bootstrapped domain session with built-in primitive types |
| `list_sessions` | `SessionTool` | Lists active domain sessions |
| `get_domain_overview` | `QueryTool` | Returns domain overview with entity/primitive/relationship counts |
| `get_entity_detail` | `QueryTool` | Returns entity properties, stages, actions, policies |
| `get_domain_analysis` | `QueryTool` | Returns analysis diagnostics (errors, warnings, info) |
| `add_entity` | `EvolveTool` | Adds a new entity type |
| `add_property` | `EvolveTool` | Adds a property to an existing entity |
| `add_stage` | `EvolveTool` | Adds a lifecycle stage to an entity |
| `add_action` | `EvolveTool` | Adds an action/operation to an entity |
| `add_action_to_stage` | `EvolveTool` | Creates a new action on a stage |
| `add_relationship` | `EvolveTool` | Adds a relationship between entities |
| `get_policy_expression` | `PolicyTool` | Returns the guard expression text of a policy |
| `add_policy` | `PolicyTool` | Adds a policy with a guard expression to an entity |
| `evaluate_policy` | `PolicyTool` | Evaluates a policy against a sample subject (VM, returns bool) |
| `apply_dsl` | `DslTool` | Applies a `.poly` DSL document, **replacing** the current session domain entirely |
| `export_dsl` | `DslTool` | Exports the current session domain as `.poly` DSL text |
| `lower_expression` | `OracleTool` | Lowers a JSON policy expression through the Syntax AST pipeline for inspection |
| `describe_expression` | `OracleTool` | Returns a structured breakdown and plain-English description of an expression |
| `describe_domain_element` | `OracleTool` | Describes a domain element (entity/stage/action/policy/relationship) |
| `simulate_policy` | `OracleTool` | Simulates a JSON expression against a subject bag (VM eval, returns bool, no session needed) |
| `get_domain_suggestions` | `QueryTool` | Returns authoring suggestions (advisory hints) identifying common gaps like missing stages, actions, or policies |
| `get_dsl_guide` | `DslTool` | Returns the product-true Phase 1a/1b DSL syntax guide — call before first `apply_dsl` |
| `create_instance` | `RuntimeTool` | Creates a runtime instance of a domain entity, registered in the session store |
| `get_instance` | `RuntimeTool` | Returns a snapshot of a runtime instance: stage, properties, status |
| `list_instances` | `RuntimeTool` | Lists all runtime instances in the session, optionally filtered by entity |
| `call_action` | `RuntimeTool` | Calls an action on a runtime instance: evaluates guards, executes effects, transitions stage |

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

**DSL tools:** `apply_dsl` (parses .poly text → evolves empty domain → analysis gate → replaces session domain; revision+1; explicit HONESTY NOTES document stage `when` not enforced, instance store not running, subscription side-effects not auto-fired), `export_dsl` (printer round-trip, no side effects). Both satisfy the invariant.

## Runtime Tools — Exercise Domain Lifecycle

The **RuntimeTool** family closes the final feedback loop: agents can create instances, inspect state, and execute actions — all within the MCP session.

### Create → Call → Observe

```text
1. apply_dsl / micro-tools  →  model in session
2. create_instance          →  instanceId + initial snapshot
3. call_action              →  effects execute, stage transitions, subscriptions fire
4. get_instance             →  observe new stage + modified properties
5. list_instances           →  enumerate all instances (optionally by entity)
```

### Instance lifecycle

- Instances are **session-scoped** — each session has its own `DomainInstanceStore`.
- The **first defined stage** is the initial stage (if stages exist).
- `call_action` resolves from the **current stage** first, then entity-level actions.
- Guard policies (action-level, stage-level, entity-level) are evaluated before effects.
- On **stage transition**: OnExit → set new stage → OnEntry → notify store subscribers.
- Stage subscription fan-out happens automatically for linked subscriber instances.
- Deleted instances (`DeleteEntityInstance` effect) are marked `isDeleted: true`.

### Honesty

- `create_instance` / `get_instance` / `list_instances` are **inspect** tools — they read state, no execution.
- `call_action` uses the **same `DomainEntityInstance.CallAction` path** as the core library — VM for assign/conditionals, direct execution for transition/create/delete/link.
