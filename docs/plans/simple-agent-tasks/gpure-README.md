# Pure Grammar product DSL — Agent Queue (`gpure-*`)

**Parent:** [`../grammar-pure-end-state.md`](../grammar-pure-end-state.md) (**direction lock — authority**)  
**Historical cutover:** [`../archive/completed-2026-08-mid/grammar-integration.md`](../archive/completed-2026-08-mid/grammar-integration.md)  
**Gate:** [`gpure-gate.md`](./gpure-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
**CORE:** [`../../CORE.md`](../../CORE.md) · **Grammar:** `Poly/Grammar/README.md` · **AGENTS:** repo root  

**Status:** **DONE 2026-08-07** — pure Grammar product path (all tasks + gate; see [`gpure-gate.md`](./gpure-gate.md)). Post-gate follow-ups executed 2026-08-08: [`gpure-followups-2026-08-07.md`](./gpure-followups-2026-08-07.md) (S1–S5, N1–N4, P1 — all `[x]`).

---

## Objective

Make product `.poly` **table-driven**: parse control flow lives in `Poly.Grammar`; DomainModeling owns **tables + handlers → IR** only — no recursive-descent **language** left in Parsing.

### End state (suite Done)

1. Expressions parse via Grammar (engine may grow: `Rule`, left-assoc, …).  
2. Effects parse via Grammar rules + handlers → same `Effect` types.  
3. `DslExpressionParser` precedence ladder is **table-guided** (Option A — each layer's operator loop runs on `MatchRule("expr-*-op")` spans); the `LeftAssoc` span tables become the live fold driver in a successor step (S5, follow-ups).  
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

### Review locks (2026-08-07 adversarial feedback — do not regress)

| ID | Rule |
|----|------|
| **B1** | Effect patterns match **head only**; bodies via nested `MatchRule("effect")` loops — never Balanced-consume body then rebuild IR without cursor (gpure-5) |
| **B2** | Dedicated `DslExprParityTests` corpus (≥15 by gpure-4); grow through gpure-7 — not “1–2 goldens” |
| **B3** | Product `not` binds operand at **add** level; `not a > b` fails — pin in parity; do not silently change |
| **F1** | Engine type is **`RuleRef`**; gate greps `RuleRef` not `class Rule` |
| **F2** | RD dual may live for parity until **gpure-7**; gpure-4 must not claim final RD deletion |
| **F4** | `RuleRef` uses **longest-match** like `TryMatch`, not ManyOf first-match; zero-width → fail |
| **F5** | LeftAssoc = flat tokens; product wire is **Option A** only (layer MatchRule) — not Option B fold-from-flat |
| **F6** | Wire tasks include explicit fail-closed negatives |
| **F7** | No `when` under effect; quantifier/`invoke` tails use Identifier **text** predicates |
| **F8** | `ExpectedTokens` / error-shape drift from expr-primary extensions must be noted and guided |
| **F9** | Printer table-parity deferred — CORE/README must not overclaim pure print |
| **F10** | Option A O(n²) re-scan OK for DSL size — no premature match-cache |

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
| **0** | [`gpure-0-inventory.md`](./gpure-0-inventory.md) | S | `[x]` |
| **1** | [`gpure-1-engine-rule-ref.md`](./gpure-1-engine-rule-ref.md) | M | `[x]` |
| **2** | [`gpure-2-engine-left-assoc.md`](./gpure-2-engine-left-assoc.md) | M | `[x]` |
| **3** | [`gpure-3-expr-grammar.md`](./gpure-3-expr-grammar.md) | L | `[x]` |
| **4** | [`gpure-4-wire-expr-parser.md`](./gpure-4-wire-expr-parser.md) | M | `[x]` |
| **5** | [`gpure-5-effect-grammar.md`](./gpure-5-effect-grammar.md) | L | `[x]` |
| **6** | [`gpure-6-open-forms-patterns.md`](./gpure-6-open-forms-patterns.md) | M | `[x]` |
| **7** | [`gpure-7-delete-rd-residual.md`](./gpure-7-delete-rd-residual.md) | M | `[x]` |
| **8** | [`gpure-8-docs-core.md`](./gpure-8-docs-core.md) | S | `[x]` |
| **G** | [`gpure-gate.md`](./gpure-gate.md) | S | `[x]` |

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
