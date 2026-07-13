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

## Tool Honesty Invariant

Every MCP tool's **Name + Description + Success** must match actual behavior:

| If the tool… | Then… |
|--------------|--------|
| Name/Description says evaluate / VM / true-false | Must call the VM path and return `data.result: bool` |
| Only inspects metadata | Must be named/described as inspect/get/describe — **never** "evaluates via VM" |
| Evaluation fails | `Success: false` (or explicit error), not success without a bool |

**Current policy tools:** `get_policy_expression` (inspect-only, no VM), `add_policy` (mutation, no eval), `evaluate_policy` (VM eval, returns bool). All three satisfy the invariant.
