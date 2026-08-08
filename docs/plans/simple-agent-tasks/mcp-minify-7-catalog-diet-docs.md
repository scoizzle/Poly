# mcp-minify-7 — Oracle/inspect diet + docs

**Difficulty:** S–M  
**Status:** `[ ]`  
**Prereq:** task 6 `[x]`  

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

**Status:** Not Started  
