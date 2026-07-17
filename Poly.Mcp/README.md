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
| `add_stage` | `EvolveTool` | Adds a lifecycle stage (optionally with parent) |
| `add_action` | `EvolveTool` | Adds an action/operation to an entity |
| `add_action_to_stage` | `EvolveTool` | Creates a new action on a stage |
| `add_relationship` | `EvolveTool` | Adds a relationship between entities |
| `get_policy_expression` | `PolicyTool` | Returns the guard expression text of a policy |
| `add_policy` | `PolicyTool` | Adds a policy with a guard expression to an entity |
| `evaluate_policy` | `PolicyTool` | Evaluates a policy against a sample subject (VM, returns bool) |
| `apply_dsl` | `DslTool` | Applies a `.poly` DSL document, **replacing** the current session domain entirely |
| `export_dsl` | `DslTool` | Exports the current session domain as `.poly` DSL text |

## Dual Authoring Path

Poly.Mcp supports two complementary ways to build a domain model, each suited to different workflows.

### Batch Path (`apply_dsl`)

Write the full domain in a single `.poly` DSL document and apply it in one shot. The session's domain is **replaced** entirely — not merged incrementally.

```
domain Orders

Customer: entity {
  Name: Text required
  Email: Text required unique
}

Order: entity {
  Total: Number
  Draft: stage {
    Submit: action { transition to Active }
  }
  Active: stage {}
}

relationship Places from Customer to Order many
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
