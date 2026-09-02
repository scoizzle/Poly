# mcp-minify — Inventory freeze notes

**Date:** 2026-08-08  
**Task:** [`mcp-minify-0-inventory.md`](./mcp-minify-0-inventory.md)  
**Source:** live grep of tree at suite start; parent plan [`../mcp-catalog-minify.md`](../mcp-catalog-minify.md) §3–§4.

## A. JSON expression call sites (`DomainExpressionJsonParser` / `ParseJson`)

```
Poly.Mcp/Tools/DomainTools.cs:1149            DomainExpressionJsonParser.ParseJson(expression)   (add_policy)
Poly.Mcp/Tools/OracleTool.cs:36               DomainExpressionJsonParser.ParseJson(expressionJson) (oracle parse path)
Poly/DomainModeling/Lowering/DomainExpressionJsonParser.cs:31   class definition
Poly.Tests/DomainModeling/Lowering/DomainExpressionJsonParserTests.cs  (test file, ~20 call sites)
```

Production call sites: **2** (DomainTools `add_policy`, OracleTool).  
Test call sites: `DomainExpressionJsonParserTests.cs` only (unit tests of the class itself).

## B. Per-type MCP add/remove tools (DELETE as separate tools)

**Add family (11):**
- `add_entity` (DomainTools.cs:533)
- `add_property` (DomainTools.cs:544)
- `add_stage` (DomainTools.cs:558)
- `add_action` (DomainTools.cs:572)
- `add_action_to_stage` (DomainTools.cs:585)
- `add_relationship` (DomainTools.cs:608)
- `add_properties` (DomainTools.cs:753)
- `add_stages` (DomainTools.cs:791)
- `add_actions_to_stages` (DomainTools.cs:828)
- `add_constraint` (DomainTools.cs:979)
- `add_policy` (DomainTools.cs:1140)

**Remove family (7):**
- `remove_entity` (DomainTools.cs:645)
- `remove_property` (DomainTools.cs:656)
- `remove_stage` (DomainTools.cs:669)
- `remove_action` (DomainTools.cs:682)
- `remove_action_from_stage` (DomainTools.cs:694)
- `remove_relationship` (DomainTools.cs:633)
- `remove_policy` (DomainTools.cs:709)

Capabilities fold into unified `add` / `remove` (kind + payload) or `apply_dsl`.

## C. Keep list (parent §3.2 core table, confirmed present)

Confirmed via `rg 'McpServerTool(Name =' Poly.Mcp` — all exist today:

| Role | Tools |
|------|--------|
| **Session** | `create_domain_session`, `list_sessions` |
| **Inspect** | `get_domain_overview`, `get_entity_detail`, `get_domain_analysis`, `get_domain_suggestions` |
| **DSL authoring** | `get_dsl_guide`, `apply_dsl`, `export_dsl` |
| **Unified evolve** | **`add`**, **`remove`** (to be built — not yet registered) |
| **Runtime** | `create_instance`, `get_instance`, `list_instances`, `link_instances`, `unlink_instances`, `invoke_action` |
| **Policy eval** | `evaluate_policy` (subject bag / instanceId — not expression IR) |
| **Optional inspect** | `get_relationships`, `get_policy_expression` |

Full live tool count at suite start: **46** registered `McpServerTool` names.

## D. Test files that use JSON expressions

Files M2/M6 must update (grep `add_policy|"property".*"op"|expressionJson|DomainExpressionJsonParser`):

- `Poly.Tests/Mcp/OracleToolTests.cs` — oracle tools with JSON expr inputs
- `Poly.Tests/Mcp/McpSmokeTests.cs` — smoke tests calling `add_policy` etc.
- `Poly.Tests/DomainModeling/Analysis/DomainSemanticLookupFailClosedTests.cs` — policy JSON expr
- `Poly.Tests/DomainModeling/Lowering/DomainExpressionJsonParserTests.cs` — delete with the class (M6)

## M0 exit

- [x] Copy final keep/drop table into suite README (`simple-agent-tasks/mcp-minify-README.md`) — already present (pick order table + hard rules).
- [x] List all tests that pass JSON expressions — see §D.
- [x] Mark grammar-integration GI-8 cancelled — parent plan header + §4.5 record it cancelled.

No production code changed (docs only).
