# mcp-minify-7 — Oracle/inspect diet + docs

**Difficulty:** S–M  
**Status:** `[x]`  
**Prereq:** task 6 `[x]`

**Done 2026-08-08.**

**A. Inspect diet (recorded):** deleted `get_domain_snapshot` (overview + entity_detail + analysis cover it; test removed). **Kept** `get_constraints` — entity_detail lists only constraint *counts*, not constraint details, so the default delete condition doesn't hold. **Kept** `get_relationships` (global edge list with source/target/cardinality not available elsewhere) and `get_policy_expression` (inspect-only).

**B. Oracle diet:** kept exactly **one** DSL expression oracle = `simulate_policy`; deleted `lower_expression` + `describe_expression` (and their tests). Deleted `analyze_effect` + `lower_effect_to_csharp` (zero test references). **Kept** `describe_domain_element` — ~15 semantics fail-closed tests in `DomainSemanticLookupFailClosedTests` depend on it (missing-metadata/not-found honesty); noted per task rule. **Kept** `export_domain_to_csharp` — `SurfaceExtensionDogfoodTests` (dogfood generation path) use it; noted. Unused helpers removed (LowerToNodeData/BuildNodeData/GetMemberName/TryAnalyze/CSharpWithAnalysis/LoweredNodeData). **Tool count: 29 → 24.**

**C. Docs:** `Poly.Mcp/README.md` catalog rewritten (24 tools, `add`/`remove` payload tables, bulk vs incremental); `poly-dsl-guide.md` + `poly-dsl-agent-guide.md` JSON-expression sections replaced with DSL-only statements; `apply_dsl`/`get_dsl_guide` descriptions fixed; parent plan §10 all ticked; `customer-trust-proof-map.md` §3.3 line added (MCP expressions = DSL only, Green).

**D. Tests:** deleted 7 tests (6 oracle + 1 snapshot); suite 1927 green.  

## Objective

Finish catalog diet (inspect/oracle) and document DSL-first + unified add/remove.

## Required reading

1. Parent plan §3.2 oracle/inspect  
2. `Poly.Mcp` README if any; `Poly.Mcp/Docs`  

## Exact steps

### A. Inspect diet (choose and record in notes)

Default decisions (follow unless inventory proves dogfood needs otherwise):

| Tool | Action |
|------|--------|
| `get_domain_snapshot` | **Delete** MCP tool if `overview`+`entity_detail`+`analysis` suffice |
| `get_constraints` | **Delete** if entity_detail lists constraints |
| `get_relationships` | **Keep** if nothing else lists relationships clearly; else delete |
| `get_policy_expression` | **Keep** (inspect only) |

### B. Oracle diet

| Rule | Action |
|------|--------|
| Max **one** expression oracle with DSL input | Keep `describe_expression` **or** `simulate_policy`; delete the rest of expression oracles |
| `analyze_effect`, `lower_effect_to_csharp`, `export_domain_to_csharp`, `describe_domain_element` | **Delete** from MCP default unless a test in dogfood critically needs one — then keep only that one and note why in progress notes |

### C. Docs

1. Update MCP-facing docs / server instructions (search `Poly.Mcp` for README, instructions strings) to say:
   - Bulk: `get_dsl_guide` → `apply_dsl`  
   - Incremental: `add` / `remove` with kind+payload  
   - Never JSON expression IR  
2. Parent plan [`../mcp-catalog-minify.md`](../mcp-catalog-minify.md) §10 success boxes → check what is done.  
3. If `customer-trust-proof-map.md` exists untracked or tracked, add one line under MCP honesty: expressions = DSL only.  
4. Fix any remaining Description strings that mention deleted tools.

### D. Tests

Fix/remove tests for deleted oracle/inspect tools; suite green.

## Verification

```bash
rg -n "JSON expression|expressionJson|add_entity|DomainExpressionJsonParser" Poly.Mcp Poly.Tests --glob '*.{cs,md}' 
# Expect: no product claims of JSON expr or add_entity tool
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Oracle/inspect diet applied and noted  
- [ ] Docs honest  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/**`, docs listed above | Reintroduce micro-tools |
| `Poly.Tests/**` | DomainModeling parser rewrite |

## Status

**Status:** Done  
