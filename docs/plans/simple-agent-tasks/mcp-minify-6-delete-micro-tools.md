# mcp-minify-6 — Delete per-type add_*/remove_* tools

**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** tasks 3, 4, 5 all `[x]`

**Done 2026-08-08:** Deleted all 18 per-type tool methods (`add_entity` … `add_policy` + `remove_entity` … `remove_policy`) from `DomainTools.cs` via a brace-safe script; kept shared cores (`AddPolicyCore`, `AddConstraintCore`, `BuildConstraint`, `Evolve`, `Field`, `MissingField`). Affordance strings across `Poly.Mcp` renamed to `add`/`remove` (sed, deduped). Removed now-unused batch spec records (PropertySpec/StageSpec/ActionToStageSpec). **Tests converted** (Python regex + manual): ~200 call sites in `McpSmokeTests`, `OracleToolTests`, `DomainSemanticLookupFailClosedTests` → unified `add`/`remove` kind+payload. Batch tests (AddProperties/AddStages/AddActionsToStages) rewritten as multiple `add` calls. `AddConstraint` → `add(kind=constraint)` with merged config. `RemovePolicy` scope tests (stage/invalid/missing-stageName) deleted — unified remove is entity-scope only. `DomainExpressionJsonParser` already deleted (task 4). **Tool count: 46 → 29.** Suite 1934 green; proof greps empty.  

## Objective

Remove all **per-type** MCP structure tools so only unified `add` / `remove` (+ `apply_dsl`) remain for evolve. Fix all tests.

## Required reading

1. Inventory notes §B (task 0)  
2. Parent plan §3.2 delete table  

## Exact steps

1. **Delete or un-register** every tool in this list (remove `[McpServerTool]` method entirely if nothing else calls it):

**Add family:**  
`add_entity`, `add_property`, `add_stage`, `add_action`, `add_action_to_stage`, `add_relationship`, `add_properties`, `add_stages`, `add_actions_to_stages`, `add_constraint`, `add_policy`

**Remove family:**  
`remove_entity`, `remove_property`, `remove_stage`, `remove_action`, `remove_action_from_stage`, `remove_relationship`, `remove_policy`

2. Update **all** tests that called those tools to use `add` / `remove` with kind+payload **or** `apply_dsl`.  

3. Update any affordance string arrays that name deleted tools.  

4. Delete `DomainExpressionJsonParser.cs` and `DomainExpressionJsonParserTests.cs` if still present.  

5. Prove clean:

```bash
rg -n 'McpServerTool\(Name = "add_entity"|McpServerTool\(Name = "add_property"|McpServerTool\(Name = "add_policy"|McpServerTool\(Name = "remove_entity"' Poly.Mcp --glob '*.cs'
# Expect: no matches

rg -n "DomainExpressionJsonParser" --glob '*.cs'
# Expect: no matches
```

6. Do **not** remove `apply_dsl`, runtime tools, session/query tools in this task.  
7. Do **not** remove `add` / `remove` unified tools.

## Verification

```bash
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Grep checks above empty  
- [ ] Suite green  
- [ ] Count registered tools documented in progress notes (optional)  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/**` | DomainModeling semantics / evolution algorithms (only call sites) |
| `Poly.Tests/**` | Grammar engine |
| Delete JsonParser files | |

## Status

**Status:** Done  
