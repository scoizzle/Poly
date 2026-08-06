# Micro-Task: Q3′ — Confirm shipped inventory (read-only)

**Suite:** [`qe-README.md`](qe-README.md) **#Q3.R0**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) Q3′  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~6k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** none  

## Objective

Document what Q3′ **already ships** in code so implementers do **not** re-build parse/print/IR. Output a short checklist into the parent plan decision log (or this file Notes).

## Required Reading

- `Poly/DomainModeling/DomainExpression.cs` — `AnyExpr` / `AllExpr` / `NoneExpr` / `CountExpr`
- `Poly/DomainModeling/Parsing/PolyDslParser.cs` — `ParseQuantifiedExpression` (~1166+)
- `Poly/DomainModeling/DomainEntityInstance.cs` — `PreprocessQuantifiers` / `EvaluateAnyExpr`
- Guide: `Poly.Mcp/Docs/poly-dsl-agent-guide.md` — § Collection Quantifiers
- Commit note: `bb5032b` Phase 4 Ship Q3′

## Exact Steps

1. Confirm product forms work in code: `any|all|none Rel where body`, `count Rel`, `count Rel where body`.
2. Confirm runtime: `EvaluatePolicy` preprocesses quantifiers against store links.
3. Confirm analysis: OneToMany only; OneToOne/unknown fail (see `PolicyConstraintAnalysisTests` Quantifier_*).
4. List **gaps only** (do not invent work): guide contradictions, MCP e2e, JSON, empty-set docs.
5. Update Notes on this file with inventory + residual IDs for R1–R4.

## Verification

- [ ] Inventory lists shipped vs residual honestly
- [ ] No code changes required for this task
- [ ] Does **not** claim “Q3′ unshipped”

## Output

- Notes on this file + optional one paragraph in `dsl-query-surface.md` decision log  
- Summary: `../agent-summaries/qe-q3-r0-summary.md`

## Out of Scope

- New features
- Re-implementing parser/IR

## Status tracking

**Claimed by:** Copilot (2026-07-23)  
**Started:** 2026-07-23  
**Notes / Blockers:**

## Shipped Q3′ inventory

### ✅ Fully shipped
| Component | Evidence | Status |
|-----------|----------|--------|
| Expression IR | `AnyExpr`, `AllExpr`, `NoneExpr`, `CountExpr` in `DomainExpression.cs` | ✅ |
| Parser | `ParseQuantifiedExpression` at line 1174 in `PolyDslParser.cs` — parses `any/all/none/count Rel [where body]` | ✅ |
| Printer | Q3′ forms render via `DomainDslPrinter` | ✅ (part of shipped commit) |
| Analysis | `DMEFF007` — OneToMany only; OneToOne/unknown rejected (`PolicyConstraintAnalysisTests Quantifier_*`) | ✅ |
| Runtime eval | `PreprocessQuantifiers` + `EvaluateAnyExpr/AllExpr/NoneExpr/CountExpr` in `DomainEntityInstance.cs` — evaluates against store-linked targets | ✅ |
| Parser unit tests | 6 `Parser_Quantifier_*` tests in `McpSmokeTests.cs` | ✅ |
| Runtime unit tests | 9 `EvaluatePolicy_*Quantifier_*` tests in `DomainEntityInstanceTests.cs` (any true/false, all true/false/empty, none, count >/) | ✅ |
| Guide (product) | Section "Collection Quantifiers (Q3′)" in `poly-dsl-agent-guide.md` with table + examples | ✅ |

### ⚠️ Contradictions / gaps in guide
| What | Where | Problem |
|------|-------|---------|
| "Full runtime evaluation via evaluate_policy/simulate_policy ... is a future enhancement" | `poly-dsl-agent-guide.md` line 315 | Contradicts table at line 341 which says "✅ Q3′ shipped" with "store-aware runtime eval" |
| "any/all/none/count over collections (Q3′ — shipped)" | `poly-dsl-agent-guide.md` line 326 | Listed under "Not yet shipped" heading — mis-placed |
| `evaluate_policy` creates bare `DomainEntityInstance.Create(entity, subjectValues)` with no Store | `DomainTools.cs` line 1120 | Q3′ evaluation throws `InvalidOperationException("Cannot resolve relationship target without a DomainInstanceStore")` — `evaluate_policy` cannot actually run Q3′ |

### Real gap
The MCP `evaluate_policy` tool (and `simulate_policy`) creates a standalone instance with no `DomainInstanceStore`. `GetOutboundRelatedInstances` in `PreprocessQuantifiers` throws when `Store is null`. Q3′ runtime evaluation **only works** inside `invoke_action` → `DomainEntityInstance.EvaluatePolicy` when the instance has been added to a store with linked targets. The guide's "future enhancement" paragraph is accurate for `evaluate_policy` — but the table says "shipped." Guide needs to reconcile this.

### Verified working paths (no action needed)
- DSL parse → `DomainExpression` AST ✅
- `apply_dsl` → analysis ✅
- `export_dsl` → `.poly` text ✅
- `create_instance` + `link_instances` + `invoke_action` → policy evaluation with Q3′ ✅
- `evaluate_policy` / `simulate_policy` → **throws** on Q3′ expressions ❌ (no store) — this is the main gap`
