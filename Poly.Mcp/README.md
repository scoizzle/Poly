# Poly.Mcp — MCP Server for Poly Domain Modeling

## Tool Surface (V3 — active)

Tools live in `Poly.Mcp/Tools/` and use only V3 types (`Poly.DomainModeling`, no `Poly.Data.Modeling`).

| Tool | Class | Purpose |
|------|-------|---------|
| `create_domain_session` | `V3SessionTool` | Creates a bootstrapped domain session with built-in primitive types |
| `list_sessions` | `V3SessionTool` | Lists active domain sessions |
| `get_domain_overview` | `V3QueryTool` | Returns domain overview with entity/primitive/relationship counts |
| `get_entity_detail` | `V3QueryTool` | Returns entity properties, stages, actions, policies |
| `get_domain_analysis` | `V3QueryTool` | Returns analysis diagnostics (errors, warnings, info) |
| `add_entity` | `V3EvolveTool` | Adds a new entity type |
| `add_property` | `V3EvolveTool` | Adds a property to an existing entity |
| `add_stage` | `V3EvolveTool` | Adds a lifecycle stage (optionally with parent) |
| `add_action` | `V3EvolveTool` | Adds an action/operation to an entity |
| `add_action_to_stage` | `V3EvolveTool` | Creates a new action on a stage |
| `add_relationship` | `V3EvolveTool` | Adds a relationship between entities |
| `get_policy_expression` | `V3EvalTool` | Returns the guard expression text of a policy |
| `add_policy` | `V3EvalTool` | Adds a policy with a guard expression to an entity |
| `evaluate_policy` | `V3EvalTool` | Evaluates a policy against a sample subject (Age) |

All tools use V3 types only (`Poly.DomainModeling`). V2 (`Poly.Data.Modeling`) has been fully removed. Session/workspace state lives in `Poly.Mcp/Sessions/` — not in DomainModeling core.
