# Micro-Task: Document shipped DSL expression grammar in product guide

**Suite:** [`qe-README.md`](qe-README.md) **#Q0.1**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §5 Q0.1 · §4.0 (planned only)  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~6k tokens  
**Status:** `[ ]` Not Started  

## Objective

Product guide must state **exactly** what boolean/scalar expressions the **shipped** DSL accepts today (property, literal, compare, `and`/`or`/`not`, `is`/`is not`, parens). Do **not** claim path-prefix, `Rel exists`, `where`, or arithmetic as product yet.

## Required Reading

- `Poly.Mcp/Docs/poly-dsl-agent-guide.md` — §7 Policies
- `Poly/DomainModeling/Parsing/PolyDslParser.cs` — expression parse methods only
- Optional: `dsl-query-surface.md` §2.1 — **do not implement Q1′**

## Exact Steps

1. Inventory parser expression operators from code (not from memory).
2. Add a clear **Expressions** subsection: grammar sketch + 3–5 examples that parse today.
3. Short “not yet” bullet list (related reads, exists, any/all, arithmetic) — one line each; detail in Q0.2.
4. Rebuild embed if needed so `get_dsl_guide` matches.

## Verification

- [ ] Guide matches parser (no `customer Tier`, `assignee exists`, `rel.Prop`, `+`, etc. as shipped)
- [ ] Examples are real parser forms
- [ ] Build green; docs-only preferred

## Output

- Updated `poly-dsl-agent-guide.md`
- Summary: `../agent-summaries/qe-q0-1-summary.md`

## Out of Scope

- Parser changes
- Planned surface prose deep-dive (Q0.2)
- JSON (Q0.3 / Q1.5)

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
