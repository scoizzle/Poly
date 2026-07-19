# Micro-Task: Q1.5 — JSON policy parity or documented split

**Suite:** [`qe-README.md`](qe-README.md) **#Q1.5**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §5 Q1.5  
**Difficulty:** Small (doc) or Medium (implement)  
**Estimated Context:** ~8k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Q1.2–Q1.3 `[x]`

## Objective

Either extend MCP JSON for path-prefix / postfix exists **or** document that JSON `add_policy` stays weaker (comparison/and/or/not only) — no silent half-support.

## Required Reading

- JSON policy parser / contract
- Guide §7 JSON table
- Product DSL forms from Q1.2–Q1.3

## Product decision

| Option | When |
|--------|------|
| **Document split** | **Default** — Q1′ ships DSL-first |
| **Implement thin JSON** | Only if shapes obvious + tests cheap |

## Exact Steps

1. Inventory JSON today.
2. Choose document vs implement.
3. Update guide + tool Description + decision log.
4. Never claim JSON supports `Rel exists` / path-prefix if it does not.

## Verification

- [ ] Honesty: Description/guide match parser
- [ ] Decision log updated
- [ ] Build + relevant tests green

## Output

- Docs and/or JSON parser + tests
- Summary: `../agent-summaries/qe-q1-5-summary.md`

## Out of Scope

- Q3′ JSON
- Changing assign effects

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
