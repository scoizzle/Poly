# Micro-Task: APM.B3 — Unconditional action hint (not error)

**Suite:** [`apm-README.md`](apm-README.md) **#B3**  
**Parent:** [`../analysis-pipeline-merge.md`](../analysis-pipeline-merge.md) Phase B DMBEH001  
**Difficulty:** Small  
**Estimated Context:** ~8k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Phase A Gate  

## Objective

Surface a **Hint** (prefer `AuthoringSuggestionAnalyzer`) when an action has **no require policies and no parameters** — never Error, never fail evolution.

## Required Reading

- Parent Phase B DMBEH001 caution  
- `AuthoringSuggestionAnalyzer.cs`  
- `BehaviorPass` / `BehaviorModel` (if reading from behavior metadata after A2)  

## Exact Steps

1. Prefer folding into authoring suggestions rather than BehaviorPass ReportError.  
2. Skip entry/exit-only or clearly internal patterns if noise is high.  
3. One positive test that suggestion/hint appears; one normal action with `require` does not.  
4. Document code `DMBEH001` or suggestion id in diagnostic codes / suggestion catalog consistently.  

## Verification

- [ ] Hint/suggestion only  
- [ ] Suite green  
- [ ] Dogfood sanity: library domain not flooded  

## Out of Scope

- Blocking missing guards  
- MCP-only messaging  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
