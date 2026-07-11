# Poly.Mcp — MCP Server for Poly Domain Modeling

## Tool Surface (V3 — active)

Tools live in `Poly.Mcp/Tools/` and use only V3 types (`Poly.DomainModeling`, no `Poly.Data.Modeling`).

| Tool | Class | Purpose |
|------|-------|---------|
| `create_domain_session` | `V3SessionTool` | Creates a bootstrapped domain session |
| `list_sessions` | `V3SessionTool` | Lists active sessions |
| `get_domain_overview` | `V3QueryTool` | Returns domain overview with entity/primitive/relationship counts |
| `get_entity_detail` | `V3QueryTool` | Returns entity properties, stages, actions, policies |
| `get_domain_analysis` | `V3QueryTool` | Returns analysis diagnostics |
| `add_entity` | `V3EvolveTool` | Adds a new entity type |
| `add_property` | `V3EvolveTool` | Adds a property to an entity |
| `add_stage` | `V3EvolveTool` | Adds a lifecycle stage |
| `add_action` | `V3EvolveTool` | Adds an action |
| `add_action_to_stage` | `V3EvolveTool` | Assigns action to a stage |
| `add_relationship` | `V3EvolveTool` | Adds a relationship between entities |

Session/workspace state lives in `Poly.Mcp/Sessions/` — not in DomainModeling core.

## Deprecated (V2 — not registered in product path)

`DomainTools.cs` contains the old V2-shaped tool surface (~80 tools, `Poly.Data.Modeling` dependency). These classes remain in the assembly for reference but are **not registered** in `Program.cs`. No new features should be added here. See `docs/plans/v2-to-v3/master-roadmap.md` Phase 5 for deletion plan.
