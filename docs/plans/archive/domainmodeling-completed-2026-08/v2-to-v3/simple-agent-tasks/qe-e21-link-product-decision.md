# Micro-Task: E2.1 — Link product path decision

**Suite:** [`qe-README.md`](qe-README.md) **#E2.1**  
**Parent:** [`../effect-surface-completeness.md`](../effect-surface-completeness.md) §5 E2.1  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~6k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** After Q0.1–Q0.2 preferred; **parallel OK** with Q0.3–Q0.5  
**No link DSL in this task.**

## Objective

Record a **product decision** for connecting existing instances: (a) create-in only, (b) bag-based link, or (c) parameter-bound link — in the effect plan decision log and product guide honesty note.

## Required Reading

- `docs/plans/v2-to-v3/effect-surface-completeness.md` §5 E2 + matrix Link/Unlink rows
- `Poly/DomainModeling/DomainEntityInstance.cs` — `ExecuteLink` / `ResolveLinkedInstance` (PropertyAccess + bag instance)
- `Poly.Mcp/Docs/poly-dsl-agent-guide.md` §8 library-only link/unlink bullets

## Product decision (pick one)

| Option | Meaning | Follow-on |
|--------|---------|-----------|
| **(a) create-in only** | Product graph write = `create in Rel` / create+RelationshipName. Link/Unlink = library/test only. | E2.2+ **Skip** until pain reopens |
| **(b) bag link** | DSL/runtime requires assign of instance-valued property then link via that property. | E2.2 implement |
| **(c) param link** | Action params bind instance targets. | Needs action params product story first |

**Default if unsure:** **(a)** — matches runtime bar, current guide advice, and query-surface rule **cross-entity writes banned** (assign never writes peers; graph writes stay create-in / explicit link).

## Exact Steps

1. Confirm runtime target rule from code (do not invent type-name link).
2. Choose (a)/(b)/(c) with one-paragraph rationale (customer pain vs bar height).
3. Write decision log row in `effect-surface-completeness.md` (date, option, notes).
4. Check E2.1 box; if (a), mark E2.2–E2.4 as deferred/non-goal with create-in substitute.
5. Align guide §8 link/unlink bullets with the decision (one sentence if (a)).
6. No parser keywords for `link`/`unlink` unless (b)/(c) and a later task owns implement.

## Verification

- [ ] Decision log has dated row
- [ ] Guide matches decision
- [ ] No pretend that compile-time entity names are runtime instances
- [ ] Docs-only (or guide-only) change set preferred

## Output

- Effect plan + guide
- Summary: `../agent-summaries/qe-e21-summary.md`

## Out of Scope

- Implementing link DSL (E2.2+)
- Invoke / E3
- Store API redesign

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
