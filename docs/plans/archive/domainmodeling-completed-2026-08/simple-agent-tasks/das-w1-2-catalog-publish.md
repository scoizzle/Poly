# DAS W1.2 — Publish domain catalog in analysis

**Wave:** W1 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §3.2  
**Difficulty:** Medium–Large  
**Status:** `[ ]`  
**Prereq:** W1.1  

## Objective

One analysis pass publishes the catalog on successful analyze. Prefer consolidating Semantic index + RuntimeContract index publication rather than a third full walk—design note rules.

## Tasks

- [ ] W1.2.1 Add catalog metadata type(s) per W1.1.
- [ ] W1.2.2 Implement publisher pass (new or folded into Semantic/RuntimeContract) with accurate `Dependencies`.
- [ ] W1.2.3 Register in `UseDomainModelAnalysisPipeline` at Stage C position (after validate, before derives that need it).
- [ ] W1.2.4 Tests: catalog present after analyze; contains known entity/relationship/action/policy entries for a fixture domain.
- [ ] W1.2.5 Fail closed: if catalog missing after analyze in a unit test harness that strips it, consumers that require it throw (may land fully in W1.3).

## Primary files

- `Poly/DomainModeling/Analysis/*` (new catalog + pass)
- `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs`
- Tests under `Poly.Tests/DomainModeling/Analysis/`

## Acceptance criteria

- [ ] Default analyze produces catalog for valid domains.
- [ ] Publisher is the single write site for catalog (grep-enforced or documented dual-write start).
- [ ] Build + tests green.

## Progress notes

(empty)
