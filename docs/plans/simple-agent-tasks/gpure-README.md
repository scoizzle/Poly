# Pure Grammar product DSL — Agent Queue (`gpure-*`)

**Parent:** [`../grammar-pure-end-state.md`](../grammar-pure-end-state.md) (**direction lock — authority**)  
**Historical cutover:** [`../archive/completed-2026-08-mid/grammar-integration.md`](../archive/completed-2026-08-mid/grammar-integration.md)  
**Gate:** [`gpure-gate.md`](./gpure-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
**CORE:** [`../../CORE.md`](../../CORE.md) · **Grammar:** `Poly/Grammar/README.md` · **AGENTS:** repo root  

**Status:** Ready to admit as **CURRENT** — finish this stream before parallel product suites when human prioritizes pure Grammar.

---

## Objective

Make product `.poly` **table-driven**: parse control flow lives in `Poly.Grammar`; DomainModeling owns **tables + handlers → IR** only — no recursive-descent **language** left in Parsing.

### End state (suite Done)

1. Expressions parse via Grammar (engine may grow: `Rule`, left-assoc, …).  
2. Effects parse via Grammar rules + handlers → same `Effect` types.  
3. `DslExpressionParser` RD precedence **gone** (or thin wrapper that only calls Matcher).  
4. Open forms prefer **patterns** over opaque RD `IExpressionPrimaryForm` where possible.  
5. Full suite green; CORE/README honest.

### Locks (do not violate)

| ID | Rule |
|----|------|
| G1 | **If pure is painful → change `Poly/Grammar`**, not grow `ParseFoo` trees |
| G2 | Handlers map `MatchResult` / cursor → IR only — no private expression language |
| G3 | Same `DomainChange` / `DomainExpression` / `Effect` product types |
| G4 | Full product DSL corpus stays green every merge (round-trip, MCP apply_dsl, policy tests) |
| G5 | No JSON expression dual media (cancelled GI-8) |
| G6 | No temporal product features in this suite (p1 separate; may use bridge until GP4) |
| G7 | One task per agent turn; file ownership absolute |

---

## How to pick

1. First `[ ]` in order **0 → G**.  
2. **One task per turn.**  
3. After each: mark `[x]`, update this table, run task verification.  
4. Gate + **pr1** before Done.

### Kickoff

```bash
copilot --agent plan-suite-until-done -p "Suite: gpure. Mode: until-done."
```

---

## Task pick order

| ID | File | Size | Status |
|----|------|------|--------|
| **0** | [`gpure-0-inventory.md`](./gpure-0-inventory.md) | S | `[ ]` |
| **1** | [`gpure-1-engine-rule-ref.md`](./gpure-1-engine-rule-ref.md) | M | `[ ]` |
| **2** | [`gpure-2-engine-left-assoc.md`](./gpure-2-engine-left-assoc.md) | M | `[ ]` |
| **3** | [`gpure-3-expr-grammar.md`](./gpure-3-expr-grammar.md) | L | `[ ]` |
| **4** | [`gpure-4-wire-expr-parser.md`](./gpure-4-wire-expr-parser.md) | M | `[ ]` |
| **5** | [`gpure-5-effect-grammar.md`](./gpure-5-effect-grammar.md) | L | `[ ]` |
| **6** | [`gpure-6-open-forms-patterns.md`](./gpure-6-open-forms-patterns.md) | M | `[ ]` |
| **7** | [`gpure-7-delete-rd-residual.md`](./gpure-7-delete-rd-residual.md) | M | `[ ]` |
| **8** | [`gpure-8-docs-core.md`](./gpure-8-docs-core.md) | S | `[ ]` |
| **G** | [`gpure-gate.md`](./gpure-gate.md) | S | `[ ]` |

---

## Hard rules

| Rule | Why |
|------|-----|
| Edit only task **File ownership** | Stop thrash |
| Engine tests in `Poly.Tests/Grammar/` | Prove capability before product port |
| Product tests stay green | No “fix later” |
| Name types for what they are | AGENTS naming |
| No `#region` | AGENTS |

---

## Done definition

Parent plan §8 checkboxes true; gate greps pass; full suite green.  
