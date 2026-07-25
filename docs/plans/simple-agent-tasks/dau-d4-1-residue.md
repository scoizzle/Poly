# Micro-Task: DAU.D4.1 — Proven residue only

**Suite:** [`dau-README.md`](dau-README.md) **#D4.1**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md) §5 Phase 4  
**Difficulty:** Small  
**Prereq:** **D3.7**  
**Status:** `[x]` — §14 EnumSubset deleted

## Objective

Delete only **proven dead** analysis residue. Do **not** delete Transport, coupling, or pack surfaces.

## Exact Steps

1. Check `EnumConstraintSubsetAnalyzer`:
   - If still always no-ops (inheritance removed), unregister + delete type + any dead diagnostic codes (e.g. DMCS002) with zero tests/callers.
2. Grep for remaining dead fixed-point paths in ConstraintQuality if still empty.
3. Do **not** remove CrossReference, Transport, Relationship capability facets “because unused.”
4. Add/adjust a test only if needed to lock inheritance-gone behavior.
5. Suite green.

## Definition of Done

- [ ] Only proven-dead code removed  
- [ ] Transport/coupling untouched  
- [ ] Build + suite green  
- [ ] `dau-README` D4.1 `[x]`  

## Out of Scope

- RestApi  
- Naming Pass→Analyzer (D4.3)  

## Review feedback (2026-07-25) — why reopened

`EnumConstraintSubsetAnalyzer` is **still registered** on both `UseDomainModelAnalysisPipeline` overloads. DoD boxes were never checked. Either delete proven-dead path or document why it stays with a real test proving it still fires.

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:** REOPEN — EnumSubset still on pipeline