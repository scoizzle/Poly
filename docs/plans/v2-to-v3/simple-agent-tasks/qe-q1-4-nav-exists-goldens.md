# Micro-Task: Q1.4 — Goldens for path-prefix, exists, where, assign rules

**Suite:** [`qe-README.md`](qe-README.md) **#Q1.4**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §5 Q1.4 · §3.1  
**Difficulty:** Medium  
**Estimated Context:** ~14k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Q1.2 + Q1.3 `[x]`; Q1.3b if where is in scope for exit

## Objective

End-to-end product proof: **cross-entity reads** work; **cross-entity writes** fail.

| Case | Assert |
|------|--------|
| Path-prefix policy | true + false (e.g. `customer Tier is "VIP"`) |
| `Rel exists` / `not Rel exists` | true + false |
| To-one `where` (if Q1.3b done) | multi-pred true + false |
| Scalar assign RHS read | `assign Label to customer Tier` (or direct API equivalent) |
| Related assign **LHS** | fail-loud / rejected |
| evaluate / simulate / require | at least one path |

## Required Reading

- Existing policy goldens, ApplyDsl, RT create/link patterns
- Q1.1 missing-link semantics
- AGENTS.md TUnit

## Exact Steps

1. Minimal domain (Ticket or Order+Customer) with store links as needed.
2. Add goldens above; prefer DomainModeling tests first; thin MCP smoke if cheap.
3. Do not claim Q3′ any/all.
4. Document intentional gaps in test names if any.

## Verification

- [ ] Read cases true/false
- [ ] Write-ban case green (rejection)
- [ ] Suite green (full or justified subset)

## Output

- Tests (+ TestHelpers only if needed)
- Summary: `../agent-summaries/qe-q1-4-summary.md`

## Out of Scope

- Q3′
- Full JSON parity (Q1.5)
- Guide prose (Q1.6) unless one-line fix

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
