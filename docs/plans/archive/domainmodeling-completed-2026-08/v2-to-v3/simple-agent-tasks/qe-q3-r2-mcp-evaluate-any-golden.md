# Micro-Task: Q3′ residual — MCP evaluate_policy golden for `any`

**Suite:** [`qe-README.md`](qe-README.md) **#Q3.R2**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) Q3′ exit  
**Difficulty:** Medium (still small-model OK if reading list followed)  
**Estimated Context:** ~12k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Q3.R0; library RT goldens already exist in `DomainEntityInstanceTests`  

## Objective

One **MCP end-to-end** golden: `apply_dsl` with `any orders where …` → create/link instances → **`evaluate_policy` true and false**. Proves product path agents use, not only direct API.

## Required Reading

- Existing: `DomainEntityInstanceTests` EvaluatePolicy_AnyQuantifier_* (pattern for store link)
- `Poly.Mcp/Tools/DomainTools.cs` — `EvaluatePolicy` / create_instance / link if any
- Runtime tools: create_instance, list/link as needed (`RuntimeTool` or domain session API)
- Existing MCP smokes: `ApplyDsl_QuantifierAuthoring_ApplyAndExport` (authoring only)

## Exact Steps

1. Minimal domain: Customer `orders: many Order`; policy `HasBig: policy { any orders where Total > 10 }`.
2. Session: apply_dsl → create Customer + Orders → link via store API available to MCP/tests.
3. evaluate_policy (or equivalent MCP tool) with subject/instance → **true** when a linked order Total > 10.
4. **false** when no match or empty links.
5. Prefer TUnit under `Poly.Tests/Mcp/` matching existing smoke style.
6. Do not expand to all/none/count unless free; **any** is the vertical.

## Verification

- [ ] True + false green
- [ ] Uses MCP/session tools agents would call (not only DomainEntityInstance API)
- [ ] Suite subset green

## Output

- Test(s)  
- Summary: `../agent-summaries/qe-q3-r2-summary.md`

## Out of Scope

- JSON quantifiers
- New IR
- Q4 aggregates

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
